using System.Net.Http.Json;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using VideoNote.Shared.Contracts;

namespace VideoNote.Client.Services;

public sealed class ApiClient(HttpClient http)
{
    public async IAsyncEnumerable<ConversationEvent> Reply(Guid id, string message, [EnumeratorCancellation] CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/tasks/{id}/conversation")
            { Content = JsonContent.Create(new ConversationInput { Message = message }) };
        request.SetBrowserResponseStreamingEnabled(true);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccess(response);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        var done = false;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var item = JsonSerializer.Deserialize<ConversationEvent>(line, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("对话服务返回空数据。");
            if (item.Type == "error") throw new InvalidOperationException(item.Text);
            if (item.Type == "done") done = true;
            yield return item;
        }
        if (!done) throw new InvalidOperationException("对话连接中断，请刷新确认历史后重试。");
    }
    public async Task<T> Get<T>(string path)
    {
        using var response = await http.GetAsync(path);
        await EnsureSuccess(response);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>()
                ?? throw new InvalidOperationException("服务端返回空响应。");
        }
        catch (JsonException) { throw new InvalidOperationException("服务端返回的数据格式无效。"); }
    }
    public async Task Send<T>(HttpMethod method, string path, T input)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(input) };
        using var response = await http.SendAsync(request);
        await EnsureSuccess(response);
    }
    public async Task Delete(string path)
    {
        using var response = await http.DeleteAsync(path);
        await EnsureSuccess(response);
    }
    public static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var message = $"请求失败（{(int)response.StatusCode}）。";
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (json.TryGetProperty("message", out var m)) message = m.GetString() ?? message;
            else if (json.TryGetProperty("errors", out var errors))
                message = string.Join(" ", errors.EnumerateObject().SelectMany(p => p.Value.EnumerateArray().Select(x => x.GetString())));
        }
        catch (JsonException) { }
        throw new InvalidOperationException(message);
    }
}
