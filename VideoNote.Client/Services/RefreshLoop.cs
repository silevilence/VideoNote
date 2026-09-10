namespace VideoNote.Client.Services;

public static class RefreshLoop
{
    public static async Task RunAsync(Func<Task> refresh, Action<Exception> failed,
        TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                try { await refresh(); }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) break;
                    failed(ex);
                }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
