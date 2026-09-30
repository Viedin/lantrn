using Lantrn.Services;

namespace Lantrn.Test;

// Uploaded documents and model answers are rendered as raw markup, so nothing in them may run script.
public class MarkdownRendererTests
{
    [Theory]
    [InlineData("[click](javascript:alert(1))")]
    [InlineData("[click](  JavaScript:alert(1))")]
    [InlineData("[click](&#106;avascript:alert(1))")]
    [InlineData("[click](data:text/html,<script>alert(1)</script>)")]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("[click][ref]\n\n[ref]: vbscript:alert(1)")]
    public void Script_links_become_plain_text(string markdown)
    {
        var html = MarkdownRenderer.ToHtml(markdown).Value;

        Assert.DoesNotContain("<a", html);
        Assert.DoesNotContain("href", html);
    }

    [Theory]
    [InlineData("[site](https://example.com/page)", "https://example.com/page")]
    [InlineData("[mail](mailto:someone@example.com)", "mailto:someone@example.com")]
    [InlineData("[relative](docs/leave.md)", "docs/leave.md")]
    [InlineData("[rooted](/docs/leave.md)", "/docs/leave.md")]
    [InlineData("[anchor](#leave)", "#leave")]
    public void Ordinary_links_are_kept(string markdown, string href)
    {
        Assert.Contains($"href=\"{href}\"", MarkdownRenderer.ToHtml(markdown).Value);
    }

    [Fact]
    public void Images_can_be_left_out()
    {
        const string markdown = "![chart](https://example.com/chart.png?q=secret)";

        Assert.Contains("<img", MarkdownRenderer.ToHtml(markdown).Value);

        var html = MarkdownRenderer.ToHtml(markdown, allowImages: false).Value;
        Assert.DoesNotContain("<img", html);
        Assert.Contains("chart", html);
    }
}
