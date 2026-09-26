using Lantrn.Services.Search;
using Microsoft.AspNetCore.Components;

namespace Lantrn.Components.Search;

// How a hit is described and linked, shared by the result list, the preview and the answer's sources.
public static class SearchFormat
{
    // "collection › Heading › Sub-heading › p. 3–4", the way a search engine shows where a result lives.
    public static string Crumb(string collection, SearchHit hit)
    {
        var parts = new List<string> { collection };
        if (!string.IsNullOrEmpty(hit.HeadingPath))
        {
            parts.Add(hit.HeadingPath);
        }
        if (hit.FirstPage is { } first)
        {
            parts.Add(hit.LastPage is { } last && last != first ? $"pp. {first}–{last}" : $"p. {first}");
        }
        return string.Join(" › ", parts);
    }

    // The document page links back here, so its back button returns to the same search.
    public static string DocumentHref(NavigationManager navigation, Guid documentId) =>
        $"documents/{documentId}?returnUrl={Uri.EscapeDataString(navigation.ToBaseRelativePath(navigation.Uri))}";
}
