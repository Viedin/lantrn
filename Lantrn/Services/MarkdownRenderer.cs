using Markdig;
using Microsoft.AspNetCore.Components;

namespace Lantrn.Services;

// Renders extracted markdown to HTML for display.
public static class MarkdownRenderer
{
    // Raw HTML is disabled: the markdown comes out of whatever document was uploaded (including
    // HTML and markdown files), so treating it as trusted HTML would let a crafted document inject script into the page.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static MarkupString ToHtml(string markdown) =>
        new(Markdown.ToHtml(markdown ?? string.Empty, Pipeline));

    public static string ToPlainText(string markdown) =>
        Markdown.ToPlainText(markdown ?? string.Empty, Pipeline);
}
