namespace VideoNote.Shared.Domain;

/// <summary>Declared capability matching is advisory when the user explicitly overrides it.</summary>
public static class ModelCapabilityRules
{
    public static bool Matches(AnalysisMode mode, bool supportsImage, bool supportsVideo) => mode switch
    {
        AnalysisMode.DirectVideo => supportsVideo,
        AnalysisMode.SampledFrames => supportsImage,
        AnalysisMode.Subtitles => true,
        _ => false
    };
    public static string RequiredCapability(AnalysisMode mode) => mode switch
    {
        AnalysisMode.DirectVideo => "视频",
        AnalysisMode.SampledFrames => "图像",
        AnalysisMode.Subtitles => "文本",
        _ => "未知"
    };
}
