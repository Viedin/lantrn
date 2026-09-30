namespace Lantrn.Components.Search;

// Filters typed into the search box, like tag:finance or in:"Team docs". The rest of the input is the query itself.
public static class SearchQuery
{
    public const string TagKey = "tag";
    public const string CollectionKey = "in";

    private static readonly string[] Keys = [TagKey, CollectionKey];

    // A filter without a value is dropped. Of several in: filters the last one counts.
    public static ParsedQuery Parse(string input)
    {
        var parts = Parts(input).ToList();
        var filters = parts.Where(p => p.Key is not null && p.Value.Length > 0).ToList();

        return new ParsedQuery(
            string.Join(' ', parts.Where(p => p.Key is null).Select(p => p.Value)),
            filters.Where(p => p.Key == TagKey).Select(p => p.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            filters.LastOrDefault(p => p.Key == CollectionKey)?.Value);
    }

    // The filter still being typed at the end of the input; a space or a closing quote finishes it.
    // A word that could become a filter, like "t" for tag:, is a draft without a key.
    public static FilterDraft? Draft(string input)
    {
        if (Parts(input).LastOrDefault() is not { Finished: false } last || last.Start + last.Length != input.Length)
        {
            return null;
        }
        if (last.Key is null && Keywords(input).Count == 0)
        {
            return null;
        }
        return new FilterDraft(last.Key, last.Value, last.Start);
    }

    // The keywords the word at the end of the input could start, leaving out filters the input already has.
    public static IReadOnlyList<string> Keywords(string input)
    {
        var parts = Parts(input).ToList();
        if (parts.LastOrDefault() is not { Key: null } last || last.Start + last.Length != input.Length)
        {
            return [];
        }

        var used = parts.Select(p => p.Key).OfType<string>().ToHashSet();
        return Keys
            .Where(k => !used.Contains(k) && k.StartsWith(last.Value, StringComparison.OrdinalIgnoreCase))
            .Select(k => $"{k}:")
            .ToList();
    }

    public static string Complete(string input, FilterDraft draft, string value) =>
        draft.Key is null
            ? $"{input[..draft.Start]}{value}"
            : $"{input[..draft.Start]}{Format(draft.Key, value)} ";

    public static string Format(string key, string value) =>
        value.Any(char.IsWhiteSpace) ? $"{key}:\"{value}\"" : $"{key}:{value}";

    // Options starting with what was typed come first. Nothing is suggested once an option is typed out in full.
    public static IReadOnlyList<string> Suggest(string partial, IEnumerable<string> options, int limit)
    {
        var matches = options.Where(o => o.Contains(partial, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Any(o => o.Equals(partial, StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        return matches
            .OrderBy(o => !o.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToList();
    }

    private static IEnumerable<Part> Parts(string input)
    {
        var i = 0;
        while (i < input.Length)
        {
            if (char.IsWhiteSpace(input[i]))
            {
                i++;
                continue;
            }

            var start = i;
            var key = Keys.FirstOrDefault(k => input.AsSpan(start).StartsWith($"{k}:", StringComparison.OrdinalIgnoreCase));
            if (key is not null)
            {
                i += key.Length + 1;
            }

            if (key is not null && i < input.Length && input[i] == '"')
            {
                var close = input.IndexOf('"', i + 1);
                var value = input[(i + 1)..(close < 0 ? input.Length : close)];
                i = close < 0 ? input.Length : close + 1;
                yield return new Part(start, i - start, key, value.Trim(), Finished: close >= 0);
                continue;
            }

            var valueStart = i;
            while (i < input.Length && !char.IsWhiteSpace(input[i]))
            {
                i++;
            }
            yield return new Part(start, i - start, key, input[valueStart..i], Finished: false);
        }
    }

    private sealed record Part(int Start, int Length, string? Key, string Value, bool Finished);
}

public sealed record ParsedQuery(string Text, IReadOnlyList<string> Tags, string? Collection);

public sealed record FilterDraft(string? Key, string Partial, int Start);
