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
            if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "mailto"))
                link.Url = "";
        }
        return document.ToHtml(Pipeline);
    }
}
