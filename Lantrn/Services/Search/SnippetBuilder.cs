using System.Net;
using System.Text;
using Microsoft.AspNetCore.Components;

namespace Lantrn.Services.Search;

// A search-engine style excerpt: the window of text holding the most query terms, with each term marked.
public static class SnippetBuilder
{
    // How much text to keep ahead of the first term in the window, so it doesn't open mid-thought.
    private const int Lead = 60;

    public static MarkupString Build(string text, IReadOnlySet<string> terms, int length = 240)
    {
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var matches = KeywordEncoder.Tokens(text).Where(token => terms.Contains(token.Text)).ToList();

        var start = BestStart(text, matches, length);
        var end = Math.Min(text.Length, start + length);

        // Snap both ends to word boundaries.
        if (start > 0 && text.IndexOf(' ', start) is var firstSpace and >= 0 && firstSpace < end)
        {
            start = firstSpace + 1;
        }
        if (end < text.Length && text.LastIndexOf(' ', end - 1, end - start) is var lastSpace && lastSpace > start)
        {
            end = lastSpace;
        }

        var html = new StringBuilder();
        if (start > 0)
        {
            html.Append("… ");
        }

        var cursor = start;
        foreach (var match in matches.Where(m => m.Start >= start && m.Start + m.Length <= end))
        {
            html.Append(WebUtility.HtmlEncode(text[cursor..match.Start]));
            html.Append("<mark>").Append(WebUtility.HtmlEncode(text.Substring(match.Start, match.Length))).Append("</mark>");
            cursor = match.Start + match.Length;
        }

        html.Append(WebUtility.HtmlEncode(text[cursor..end]));
        if (end < text.Length)
        {
            html.Append(" …");
        }

        return new MarkupString(html.ToString());
    }

    private static int BestStart(string text, List<Token> matches, int length)
    {
        if (matches.Count == 0)
        {
            return 0;
        }

        var best = matches[0];
        var bestCount = 0;
        foreach (var candidate in matches)
        {
            var windowEnd = candidate.Start + length - Lead;
            var count = matches.Count(m => m.Start >= candidate.Start && m.Start + m.Length <= windowEnd);
            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        // Near the end of the text, slide back so the window stays full.
        return Math.Max(0, Math.Min(best.Start - Lead, text.Length - length));
    }
}
