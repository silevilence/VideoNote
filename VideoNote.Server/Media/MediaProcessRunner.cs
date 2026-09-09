using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace VideoNote.Server.Media;

public sealed class MediaProcessRunner(IOptions<FfmpegOptions> options)
{
    public async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken ct)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        }};
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            ct.ThrowIfCancellationRequested();
            process.Start();
        }
        catch (Win32Exception) { throw new InvalidOperationException("无法启动 FFmpeg/FFprobe，请检查程序路径配置。"); }
        var stdout = DrainAsync(process.StandardOutput, 2_000_000);
        var stderr = DrainAsync(process.StandardError, 8192);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException($"媒体处理失败（退出码 {process.ExitCode}）：{error}");
            return output;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException("媒体处理超时，请调整 Ffmpeg:TimeoutSeconds。");
        }
    }

    private static async Task<string> DrainAsync(StreamReader reader, int limit)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            text.Append(buffer, 0, read);
            if (text.Length > limit) text.Remove(0, text.Length - limit);
        }
        return text.ToString();
    }
}
