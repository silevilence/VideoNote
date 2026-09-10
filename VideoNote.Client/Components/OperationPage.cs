using Microsoft.AspNetCore.Components;

namespace VideoNote.Client.Components;

public abstract class OperationPage : ComponentBase
{
    protected bool Busy { get; private set; }
    protected string? Error { get; private set; }
    protected string? Notice { get; set; }
    protected string? RefreshError { get; private set; }

    protected async Task Refresh(Func<Task> action)
    {
        if (Busy) return;
        Busy = true;
        try { await action(); RefreshError = null; }
        catch (TaskCanceledException) { RefreshError = "自动刷新超时，将继续重试。"; }
        catch (HttpRequestException) { RefreshError = "自动刷新连接失败，将继续重试。"; }
        catch (InvalidOperationException ex) { RefreshError = ex.Message; }
        finally { Busy = false; }
    }

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
