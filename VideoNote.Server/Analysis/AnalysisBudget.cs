using System.Text;
using Microsoft.Extensions.Options;
namespace VideoNote.Server.Analysis;

public sealed class PipelineOptions
{
    public int MaxOutputTokens { get; set; } = 2048;
    public int MaxImagesPerSegment { get; set; } = 8;
    public int ImageTokenEstimate { get; set; } = 2048;
    public int AudioTokensPerSecond { get; set; } = 64;
    public int VideoTokensPerSecond { get; set; } = 512;
    public int RequestTimeoutSeconds { get; set; } = 300;
}
public sealed class AnalysisBudget
{
    public int Input { get; }
    public int Output { get; }
    public AnalysisBudget(int contextWindow, string prompt, PipelineOptions options)
    {
        Output = Math.Min(options.MaxOutputTokens, contextWindow / 4);
        // UTF-8 bytes conservatively bound text token usage; reserve headroom for protocol and instructions.
        Input = (int)(contextWindow * 0.75) - Output - Encoding.UTF8.GetByteCount(prompt) - 512;
        if (Input < 512 || Output < 128)
            throw new AnalysisException("模型上下文窗口过小或提示词过长，请调整模型窗口或缩短提示词。");
    }
    public static int Size(string text) => Encoding.UTF8.GetByteCount(text);
    public static IEnumerable<string> Split(string text, int bytes)
    {
        if (bytes < 4) throw new AnalysisException("上下文预算不足。");
        if (string.IsNullOrEmpty(text)) { yield return ""; yield break; }
        var buffer = new StringBuilder();
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count + rune.Utf8SequenceLength > bytes)
            { yield return buffer.ToString(); buffer.Clear(); count = 0; }
            buffer.Append(rune.ToString()); count += rune.Utf8SequenceLength;
        }
        if (buffer.Length > 0) yield return buffer.ToString();
    }
}
