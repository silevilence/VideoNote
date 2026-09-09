using System.ClientModel;
using Google.GenAI.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using VideoNote.Server.Configuration;
using VideoNote.Server.Data;
using VideoNote.Shared.Domain;

namespace VideoNote.Server.AI;

public interface IModelChatClientFactory
{
    Task<IChatClient> CreateAsync(Guid modelConfigId, CancellationToken cancellationToken = default);
}

/// <summary>Builds a disposable client snapshot from current database settings on every call.</summary>
public sealed class ChatClientFactory(VideoNoteDbContext db, ProviderSecrets secrets) : IModelChatClientFactory
{
    public async Task<IChatClient> CreateAsync(Guid modelConfigId, CancellationToken cancellationToken = default)
    {
        var model = await db.ModelConfigs.AsNoTracking().Include(m => m.Provider)
            .SingleOrDefaultAsync(m => m.Id == modelConfigId, cancellationToken)
            ?? throw new InvalidOperationException("模型配置不存在，可能已被删除。");
        var key = secrets.Resolve(model.Provider.ApiKey);
        var endpoint = new Uri(model.Provider.BaseUrl.TrimEnd('/') + "/");
        IChatClient client = model.Provider.Protocol switch
        {
            ProviderProtocol.OpenAiCompatible => new OpenAIClient(
                new ApiKeyCredential(string.IsNullOrEmpty(key) ? "not-required" : key),
                new OpenAIClientOptions { Endpoint = endpoint })
                .GetChatClient(model.ModelId).AsIChatClient(),
            ProviderProtocol.GeminiNative => new Google.GenAI.Client(
                vertexAI: false, apiKey: key,
                httpOptions: GeminiOptions(endpoint)).AsIChatClient(model.ModelId),
            _ => throw new InvalidOperationException("不支持该提供商协议。")
        };
        return new VideoInputGuard(client);
    }

    private static HttpOptions GeminiOptions(Uri endpoint)
    {
        // Accept either the service root or a versioned Gemini base URL without doubling the version.
        var path = endpoint.AbsolutePath.TrimEnd('/');
        var last = path.Split('/').Last();
        return last is "v1" or "v1beta" or "v1alpha"
            ? new HttpOptions { BaseUrl = endpoint.GetLeftPart(UriPartial.Authority) + path[..^(last.Length + 1)],
                ApiVersion = last, Timeout = 120000 }
            : new HttpOptions { BaseUrl = endpoint.ToString().TrimEnd('/'), ApiVersion = "v1beta", Timeout = 120000 };
    }

    private sealed class VideoInputGuard(IChatClient inner) : DelegatingChatClient(inner)
    {
        private static ChatMessage[] Validate(IEnumerable<ChatMessage> messages)
        {
            var snapshot = messages.ToArray();
            if (snapshot.SelectMany(m => m.Contents).OfType<DataContent>()
                .Any(c => c.MediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)))
                throw new NotSupportedException("视频必须先通过 Gemini File API 上传，再传入文件引用；不支持内联视频。");
            return snapshot;
        }
        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            base.GetResponseAsync(Validate(messages), options, cancellationToken);

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            base.GetStreamingResponseAsync(Validate(messages), options, cancellationToken);
    }
}
