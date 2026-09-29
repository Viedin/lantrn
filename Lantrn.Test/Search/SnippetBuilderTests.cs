using Lantrn.Services.Search;

namespace Lantrn.Test.Search;

// Snippets are rendered as raw markup, so everything but the marks must come out encoded.
public class SnippetBuilderTests
{
    [Fact]
    public void Document_text_is_encoded()
    {
        var snippet = Build("<script>alert('invoice')</script> & <b>invoice</b>", "invoice");

        Assert.DoesNotContain("<script>", snippet);
        Assert.DoesNotContain("<b>", snippet);
        Assert.Contains("&lt;script&gt;", snippet);
        Assert.Contains("&amp;", snippet);
        Assert.Contains("<mark>invoice</mark>", snippet);
    }

    [Fact]
    public void Marks_whole_words_whatever_their_case()
    {
        var snippet = Build("Invoice, invoices and INVOICE.", "invoice");

        Assert.Equal("<mark>Invoice</mark>, invoices and <mark>INVOICE</mark>.", snippet);
    }

    [Fact]
    public void Short_text_is_shown_whole_with_its_whitespace_collapsed()
    {
        var snippet = Build("  Refund\n\n policy\tapplies  ", "missing");

        Assert.Equal("Refund policy applies", snippet);
    }

    [Fact]
    public void Long_text_is_cut_to_the_window_with_the_most_terms()
    {
        var filler = string.Join(' ', Enumerable.Repeat("lorem", 200));
        var text = $"refund {filler} the refund policy for a late refund is simple {filler}";

        var snippet = Build(text, "refund", "policy");

        Assert.StartsWith("… ", snippet);
        Assert.EndsWith(" …", snippet);
        Assert.Contains("the <mark>refund</mark> <mark>policy</mark> for a late <mark>refund</mark>", snippet);
    }

    [Fact]
    public void The_window_never_cuts_a_word()
    {
        var text = string.Join(' ', Enumerable.Range(0, 200).Select(i => $"word{i}"));

        var snippet = Build(text, "word100");

        var words = snippet.Trim('…', ' ').Split(' ');
        Assert.All(words, w => Assert.Matches(@"^(word\d+|<mark>word100</mark>)$", w));
    }

    private static string Build(string text, params string[] terms) =>
        SnippetBuilder.Build(text, terms.ToHashSet()).Value;
}
