using Microsoft.AspNetCore.Components;

namespace VideoNote.Client.Components;

public abstract class OperationPage : ComponentBase
{
    protected bool Busy { get; private set; }
    protected string? Error { get; private set; }
    protected string? Notice { get; set; }

    protected async Task Run(Func<Task> action)
    {
        if (Busy) return;
        Busy = true; Error = null; Notice = null;
        try { await action(); }
        catch (TaskCanceledException) { Error = "请求超时，请稍后重试。"; }
        catch (HttpRequestException) { Error = "无法连接服务端，请稍后重试。"; }
        catch (InvalidOperationException ex) { Error = ex.Message; }
        finally { Busy = false; }
    }
}
