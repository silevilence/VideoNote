using VideoNote.Client.Components;
using VideoNote.Client.Services;
namespace VideoNote.Server.Tests.Analysis;

public sealed class ClientRefreshTests
{
    [Fact]
    public async Task Background_refresh_preserves_action_errors_and_notices_and_recovers_its_own_error()
    {
        var page = new PageProbe();
        await page.UserAction(() => throw new InvalidOperationException("delete conflict"));
        page.SetNotice("saved");
        await page.Background(() => throw new HttpRequestException("offline"));
        Assert.Equal("delete conflict", page.ActionError);
        Assert.Equal("saved", page.ActionNotice);
        Assert.NotNull(page.BackgroundError);
        await page.Background(() => Task.CompletedTask);
        Assert.Equal("delete conflict", page.ActionError);
        Assert.Equal("saved", page.ActionNotice);
        Assert.Null(page.BackgroundError);
    }

    [Fact]
    public async Task Polling_survives_callback_failure_and_stops_cleanly()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var calls = 0; var failures = 0;
        await RefreshLoop.RunAsync(() =>
        {
            if (++calls == 1) throw new FormatException("simulated callback error");
            stop.Cancel(); return Task.CompletedTask;
        }, _ => failures++, TimeSpan.FromMilliseconds(10), stop.Token);
        Assert.Equal(2, calls);
        Assert.Equal(1, failures);
    }

    [Fact]
    public async Task Polling_disposal_tolerates_a_callback_fault_racing_with_cancellation()
    {
        using var stop = new CancellationTokenSource();
        await RefreshLoop.RunAsync(() =>
        {
            stop.Cancel();
            throw new InvalidOperationException("component disposed");
        }, _ => Assert.Fail("Shutdown faults must not be retried"), TimeSpan.FromMilliseconds(10), stop.Token);
    }

    private sealed class PageProbe : OperationPage
    {
        public string? ActionError => Error;
        public string? ActionNotice => Notice;
        public string? BackgroundError => RefreshError;
        public void SetNotice(string notice) => Notice = notice;
        public Task UserAction(Func<Task> action) => Run(action);
        public Task Background(Func<Task> action) => Refresh(action);
    }
}
