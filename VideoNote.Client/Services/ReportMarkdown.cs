using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace VideoNote.Client.Services;

public static class ReportMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().DisableHtml().Build();

    public static string Render(string text)
    {
        var document = Markdown.Parse(text, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            // Reports are untrusted model output. No executable schemes or remote image loads.
            link.IsImage = false;
            if (!SafeUrl(link.Url)) link.Url = "";
        }
        foreach (var link in document.Descendants<AutolinkInline>())
            if (!link.IsEmail && !SafeUrl(link.Url)) link.Url = "";
        return document.ToHtml(Pipeline);
    }

    private static bool SafeUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http" or "mailto";
}
