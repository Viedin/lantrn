using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
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

    private static readonly HashSet<string> SafeSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    // Markdig keeps link targets as written, so a "javascript:" link would run script when clicked; those show as plain text.
    // Without images, one that a model was talked into writing can't load from another server, carrying document text in its URL.
    public static MarkupString ToHtml(string markdown, bool allowImages = true)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            if (!IsSafeUrl(link.Url) || (link.IsImage && !allowImages))
            {
                link.ReplaceBy(new ContainerInline());
            }
        }

        foreach (var autolink in document.Descendants<AutolinkInline>().ToList())
        {
            if (!IsSafeUrl(autolink.Url))
            {
                autolink.ReplaceBy(new LiteralInline(autolink.Url));
            }
        }

        return new(document.ToHtml(Pipeline));
    }

    public static string ToPlainText(string markdown) =>
        Markdown.ToPlainText(markdown ?? string.Empty, Pipeline);

    // Relative links have no scheme. Browsers ignore whitespace and control characters in a scheme, so they are ignored here too.
    private static bool IsSafeUrl(string? url)
    {
        var compact = string.Concat((url ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)));
        var end = compact.IndexOfAny([':', '/', '?', '#']);
        return end < 0 || compact[end] != ':' || SafeSchemes.Contains(compact[..end]);
    }
}
