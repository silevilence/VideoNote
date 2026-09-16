using System.Diagnostics;
using VideoNote.Shared.Contracts;
using VideoNote.Shared.Domain;

namespace VideoNote.Client.Components;

/// <summary>页面共用的状态、模式文案与格式化辅助。</summary>
public static class Ui
{
    public static string StatusName(AnalysisTaskStatus status) => status switch
    {
        AnalysisTaskStatus.Queued => "排队中",
        AnalysisTaskStatus.Preprocessing => "预处理",
        AnalysisTaskStatus.Understanding => "理解中",
        AnalysisTaskStatus.Combining => "组合中",
        AnalysisTaskStatus.Completed => "已完成",
        AnalysisTaskStatus.Failed => "失败",
        AnalysisTaskStatus.Canceled => "已取消",
        _ => throw new UnreachableException($"未知的任务状态：{status}")
    };
    /// <summary>运行或排队中的任务。</summary>
    public static bool IsActive(AnalysisTaskStatus status) =>
        IsRunning(status) || status == AnalysisTaskStatus.Queued;

    public static string StatusClass(AnalysisTaskStatus status) =>
        $"chip {StatusKey(status)}{(IsRunning(status) ? " st-running" : "")}";

    private static string StatusKey(AnalysisTaskStatus status) => status switch
    {
        AnalysisTaskStatus.Queued => "st-queued",
        AnalysisTaskStatus.Preprocessing => "st-preprocessing",
        AnalysisTaskStatus.Understanding => "st-understanding",
        AnalysisTaskStatus.Combining => "st-combining",
        AnalysisTaskStatus.Completed => "st-completed",
        AnalysisTaskStatus.Failed => "st-failed",
        AnalysisTaskStatus.Canceled => "st-canceled",
        _ => throw new UnreachableException($"未知的任务状态：{status}")
    };
    public static bool IsRunning(AnalysisTaskStatus status) =>
        status is AnalysisTaskStatus.Preprocessing or AnalysisTaskStatus.Understanding or AnalysisTaskStatus.Combining;

    public static string ModeName(AnalysisMode mode) => mode switch
    {
        AnalysisMode.DirectVideo => "直接理解",
        AnalysisMode.SampledFrames => "抽帧理解",
        _ => "字幕理解"
    };

    public static string ModeClass(AnalysisMode mode) => mode switch
    {
        AnalysisMode.DirectVideo => "chip chip-amber",
        AnalysisMode.SampledFrames => "chip chip-blue",
        _ => "chip chip-violet"
    };

    /// <summary>模式简介与所需模型能力。</summary>
    public static (string Title, string Description, string Need) ModeInfo(AnalysisMode mode) =>
        mode switch
        {
            AnalysisMode.DirectVideo =>
                ("直接理解", "整段视频交给多模态模型通读，保留完整时空信息，适合短片与关键镜头。", "视频"),
            AnalysisMode.SampledFrames =>
                ("抽帧理解", "按帧率抽帧并提取音频，画面逐组理解后汇总，通用性最好。", "图像"),
            _ =>
                ("字幕理解", "以内嵌字幕或转写文本为准，速度最快，token 消耗最低。", "文本")
        };

    public static bool ModelMatches(ModelDto model, AnalysisMode mode) => ModelCapabilityRules.Matches(mode, model.SupportsImage, model.SupportsVideo);

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):F1} KB",
        _ => $"{bytes} B"
    };

    public static string Elapsed(TaskDto task)
    {
        if (task.StartedAtUtc is not { } start) return "—";
        var duration = (task.CompletedAtUtc ?? DateTime.UtcNow) - start;
        return $"{Math.Max(0, (int)duration.TotalHours):00}:{Math.Max(0, duration.Minutes):00}:{Math.Max(0, duration.Seconds):00}";
    }

    public static string Time(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
