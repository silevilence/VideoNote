using System.Globalization;
using System.Text.RegularExpressions;
using VideoNote.Server.Transcription;
namespace VideoNote.Server.Analysis;

public static partial class SubtitleParser
{
    public static IReadOnlyList<TimedText> Parse(string srt)
    {
        var result = new List<TimedText>();
        foreach (var block in Regex.Split(srt.Trim().Replace("\r", ""), @"\n\s*\n"))
        {
            if (string.IsNullOrWhiteSpace(block)) continue;
            var lines = block.Split('\n');
            var timing = Array.FindIndex(lines, l => l.Contains("-->"));
            if (timing < 0 || timing == lines.Length - 1) throw new AnalysisException("字幕格式无效：缺少时间戳或正文。");
            var match = Timing().Match(lines[timing]);
            if (!match.Success) throw new AnalysisException("字幕时间戳格式无效。");
            var start = Seconds(match.Groups[1].Value);
            var end = Seconds(match.Groups[2].Value);
            if (end <= start) throw new AnalysisException("字幕结束时间必须晚于开始时间。");
            var text = string.Join("\n", lines.Skip(timing + 1)).Trim();
            if (text.Length > 0) result.Add(new(start, end, text));
        }
        return result.OrderBy(t => t.StartSeconds).ToArray();
    }
    private static double Seconds(string text)
    {
        var parts = text.Replace(',', '.').Split(':');
        if (!double.TryParse(parts[0], CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], out var minutes) || minutes >= 60 ||
            !double.TryParse(parts[2], CultureInfo.InvariantCulture, out var seconds) || seconds >= 60)
            throw new AnalysisException("字幕时间戳数值无效。");
        return hours * 3600 + minutes * 60 + seconds;
    }
    [GeneratedRegex(@"^(\d{2,}:\d{2}:\d{2}[,.]\d{3})\s+-->\s+(\d{2,}:\d{2}:\d{2}[,.]\d{3})(?:\s.*)?$")]
    private static partial Regex Timing();
}
