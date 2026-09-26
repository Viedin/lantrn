using System.Text;

namespace Lantrn.Services.Search;

// Turns text into a sparse keyword vector for Qdrant, which weighs each term by its rarity (IDF) on its side.
// Dense vectors blur exact tokens; this catches names, amounts and codes a query spells out literally.
public static class KeywordEncoder
{
    // BM25 term-frequency saturation; the average length is a fixed guess, as Qdrant only supplies the IDF half.
    private const float K1 = 1.2f;
    private const float B = 0.75f;
    private const float AverageLength = 256f;

    public static SparseText EncodeDocument(string text)
    {
        var tokens = Tokenize(text);
        var lengthNorm = 1f - B + B * tokens.Count / AverageLength;

        // Grouped by hash, not token: two tokens colliding must become one index, which Qdrant requires to be unique.
        var terms = tokens
            .GroupBy(Hash)
            .Select(g => (Index: g.Key, Weight: g.Count() * (K1 + 1f) / (g.Count() + K1 * lengthNorm)))
            .OrderBy(t => t.Index)
            .ToList();

        return new SparseText(terms.Select(t => t.Weight).ToArray(), terms.Select(t => t.Index).ToArray());
    }

    public static SparseText EncodeQuery(string text)
    {
        var indices = Tokenize(text).Select(Hash).Distinct().Order().ToArray();
        return new SparseText(Enumerable.Repeat(1f, indices.Length).ToArray(), indices);
    }

    // The same tokens the keyword vector matches on, so highlighting agrees with what was searched for.
    public static IReadOnlySet<string> QueryTerms(string text) => Tokenize(text).ToHashSet();

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

    private static List<string> Tokenize(string text) => Tokens(text).Select(t => t.Text).ToList();

    // FNV-1a: string.GetHashCode is randomized per process, and these indices are stored.
    private static uint Hash(string token)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(token))
        {
            hash = (hash ^ b) * 16777619u;
        }
        return hash;
    }
}

public sealed record SparseText(float[] Values, uint[] Indices);

public readonly record struct Token(int Start, int Length, string Text);
