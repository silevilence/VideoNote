using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using VideoNote.Server.Analysis;
using VideoNote.Server.Configuration;
using VideoNote.Server.Data.Entities;
namespace VideoNote.Server.AI;

public sealed record GeminiFile(string Name, Uri Uri);
public interface IGeminiFileService
{
    Task<GeminiFile> UploadAsync(Provider provider, string path, CancellationToken ct);
    Task DeleteAsync(Provider provider, GeminiFile file, CancellationToken ct);
}
public sealed class GeminiFileService(HttpClient http, ProviderSecrets secrets) : IGeminiFileService
{
    private static (string Root, string Version) Endpoint(string baseUrl)
    {
        var uri = new Uri(baseUrl.TrimEnd('/'));
        var path = uri.AbsolutePath.TrimEnd('/');
        var last = path.Split('/').Last();
        var versioned = last is "v1" or "v1beta" or "v1alpha";
        return (uri.GetLeftPart(UriPartial.Authority) + (versioned ? path[..^(last.Length + 1)] : path),
            versioned ? last : "v1beta");
    }
    private HttpRequestMessage Request(HttpMethod method, string uri, Provider provider)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("x-goog-api-key", secrets.Resolve(provider.ApiKey));
        return request;
    }
    public async Task<GeminiFile> UploadAsync(Provider provider, string path, CancellationToken ct)
    {
        var (root, version) = Endpoint(provider.BaseUrl);
        GeminiFile? uploaded = null;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
            using var start = Request(HttpMethod.Post, $"{root}/upload/{version}/files", provider);
            start.Headers.Add("X-Goog-Upload-Protocol", "resumable");
            start.Headers.Add("X-Goog-Upload-Command", "start");
            start.Headers.Add("X-Goog-Upload-Header-Content-Length", stream.Length.ToString(CultureInfo.InvariantCulture));
            start.Headers.Add("X-Goog-Upload-Header-Content-Type", "video/mp4");
            start.Content = JsonContent.Create(new { file = new { display_name = Path.GetFileName(path) } });
            using var initial = await http.SendAsync(start, ct);
            Ensure(initial);
            if (!initial.Headers.TryGetValues("X-Goog-Upload-URL", out var values) ||
                !Uri.TryCreate(values.Single(), UriKind.Absolute, out var uploadUri) ||
                uploadUri.Scheme is not ("http" or "https"))
                throw new AnalysisException("Gemini 未返回有效文件上传地址。");
            // Do not forward the provider key to the resumable session URL.
            using var upload = new HttpRequestMessage(HttpMethod.Post, uploadUri) { Content = new StreamContent(stream) };
            upload.Headers.Add("X-Goog-Upload-Offset", "0");
            upload.Headers.Add("X-Goog-Upload-Command", "upload, finalize");
            upload.Content.Headers.ContentLength = stream.Length;
            using var result = await http.SendAsync(upload, ct);
            Ensure(result);
            using var doc = await JsonDocument.ParseAsync(await result.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var file = doc.RootElement.GetProperty("file");
            uploaded = Read(file);
            while (file.GetProperty("state").GetString() == "PROCESSING")
            {
                await Task.Delay(1000, ct);
                using var poll = Request(HttpMethod.Get, $"{root}/{version}/{uploaded.Name}", provider);
                using var response = await http.SendAsync(poll, ct);
                Ensure(response);
                using var state = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                file = state.RootElement.Clone();
            }
            if (file.GetProperty("state").GetString() != "ACTIVE")
                throw new AnalysisException("Gemini 视频文件处理失败。");
            return uploaded;
        }
        catch
        {
            if (uploaded is not null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await DeleteAsync(provider, uploaded, cleanup.Token); } catch { }
            }
            throw;
        }
    }
    private static GeminiFile Read(JsonElement file)
    {
        var name = file.GetProperty("name").GetString()!;
        if (!name.StartsWith("files/", StringComparison.Ordinal) ||
            name[6..].Length == 0 || name[6..].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new AnalysisException("Gemini 文件名称无效。");
        if (!Uri.TryCreate(file.GetProperty("uri").GetString(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new AnalysisException("Gemini 文件引用无效。");
        return new(name, uri);
    }
    public async Task DeleteAsync(Provider provider, GeminiFile file, CancellationToken ct)
    {
        var (root, version) = Endpoint(provider.BaseUrl);
        using var request = Request(HttpMethod.Delete, $"{root}/{version}/{file.Name}", provider);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound) Ensure(response);
    }
    private static void Ensure(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new AnalysisException($"Gemini 文件服务返回 HTTP {(int)response.StatusCode}，请检查提供商配置与网络。");
    }
}
