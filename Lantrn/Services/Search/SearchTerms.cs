namespace Lantrn.Services.Search;

// Splits text into the words a query highlights. Qdrant does its own tokenizing and stemming for keyword matching.
public static class SearchTerms
{
    public static IReadOnlySet<string> Of(string query) => Tokens(query).Select(t => t.Text).ToHashSet();

    // Lowercased runs of letters and digits, with where they sit in the text; single letters are dropped
    // as noise, single digits kept.
    public static IEnumerable<Token> Tokens(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && char.IsLetterOrDigit(text[i]))
            {
                if (start < 0)
                {
                    start = i;
                }
                continue;
            }

            if (start >= 0 && (i - start > 1 || char.IsDigit(text[start])))
            {
                yield return new Token(start, i - start, text[start..i].ToLowerInvariant());
            }
            start = -1;
        }
    }
}

public readonly record struct Token(int Start, int Length, string Text);
