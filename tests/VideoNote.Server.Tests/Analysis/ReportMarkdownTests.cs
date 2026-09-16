using VideoNote.Client.Services;

namespace VideoNote.Server.Tests.Analysis;

public sealed class ReportMarkdownTests
{
    [Fact]
    public void Reports_render_structure_without_active_model_supplied_html()
    {
        var html = ReportMarkdown.Render("# 标题\n\n**重点**\n\n|列|值|\n|-|-|\n|甲|乙|\n\n<script>alert(1)</script>\n\n[危险](javascript:alert) ![图](https://example.com/pixel)");
        Assert.Contains("<h1>标题</h1>", html);
        Assert.Contains("<strong>重点</strong>", html);
        Assert.Contains("<table>", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("href=\"javascript:", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("href=\"https://example.com", html);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,boom")]
    [InlineData("file:///C:/secret")]
    [InlineData("vbscript:boom")]
    public void Unsafe_link_schemes_are_removed(string url)
    {
        Assert.DoesNotContain($"href=\"{url}", ReportMarkdown.Render($"[link]({url})"));
        Assert.DoesNotContain($"href=\"{url}", ReportMarkdown.Render($"<{url}>"));
    }
}
