using Lantrn.Components.Search;

namespace Lantrn.Test.Search;

public class SearchQueryTests
{
    [Fact]
    public void Filters_are_lifted_out_of_the_query()
    {
        var parsed = SearchQuery.Parse("invoice tag:finance totals TAG:2024 in:\"Team docs\"");

        Assert.Equal("invoice totals", parsed.Text);
        Assert.Equal(["finance", "2024"], parsed.Tags);
        Assert.Equal("Team docs", parsed.Collection);
    }

    [Fact]
    public void The_last_collection_counts_and_empty_filters_are_dropped()
    {
        var parsed = SearchQuery.Parse("in:first tag: refund in:second");

        Assert.Equal("refund", parsed.Text);
        Assert.Empty(parsed.Tags);
        Assert.Equal("second", parsed.Collection);
    }

    [Fact]
    public void Words_merely_containing_a_key_stay_in_the_query()
    {
        var parsed = SearchQuery.Parse("hashtag:x within:y");

        Assert.Equal("hashtag:x within:y", parsed.Text);
        Assert.Empty(parsed.Tags);
        Assert.Null(parsed.Collection);
    }

    [Theory]
    [InlineData("refund tag:fin", "tag", "fin", 7)]
    [InlineData("in:", "in", "", 0)]
    [InlineData("in:\"Team d", "in", "Team d", 0)]
    public void The_filter_at_the_end_is_a_draft(string input, string key, string partial, int start)
    {
        Assert.Equal(new FilterDraft(key, partial, start), SearchQuery.Draft(input));
    }

    [Theory]
    [InlineData("refund")]
    [InlineData("tag:finance ")]
    [InlineData("in:\"Team docs\"")]
    [InlineData("tag:finance refund")]
    public void Finished_filters_and_plain_words_are_not_drafts(string input)
    {
        Assert.Null(SearchQuery.Draft(input));
    }

    [Theory]
    [InlineData("refund i", "in:")]
    [InlineData("T", "tag:")]
    [InlineData("refund tag", "tag:")]
    public void A_word_starting_a_keyword_suggests_it(string input, string keyword)
    {
        var draft = SearchQuery.Draft(input);

        Assert.NotNull(draft);
        Assert.Null(draft.Key);
        Assert.Equal([keyword], SearchQuery.Keywords(input));
    }

    [Theory]
    [InlineData("in:Docs refund i")]
    [InlineData("tag:finance refund t")]
    public void A_keyword_already_used_is_not_suggested_again(string input)
    {
        Assert.Empty(SearchQuery.Keywords(input));
        Assert.Null(SearchQuery.Draft(input));
    }

    [Fact]
    public void Completing_a_keyword_opens_its_values()
    {
        var input = "refund t";

        var completed = SearchQuery.Complete(input, SearchQuery.Draft(input)!, "tag:");

        Assert.Equal("refund tag:", completed);
        Assert.Equal(new FilterDraft("tag", "", 7), SearchQuery.Draft(completed));
    }

    [Fact]
    public void Completing_quotes_names_with_spaces()
    {
        var input = "refund in:te";

        var completed = SearchQuery.Complete(input, SearchQuery.Draft(input)!, "Team docs");

        Assert.Equal("refund in:\"Team docs\" ", completed);
        Assert.Equal("Team docs", SearchQuery.Parse(completed).Collection);
    }

    [Fact]
    public void Suggestions_starting_with_the_input_come_first()
    {
        var suggestions = SearchQuery.Suggest("fin", ["refinance", "finance", "hr", "final"], 8);

        Assert.Equal(["finance", "final", "refinance"], suggestions);
    }

    [Fact]
    public void Nothing_is_suggested_once_an_option_is_typed_out()
    {
        Assert.Empty(SearchQuery.Suggest("Finance", ["finance", "finance-2024"], 8));
    }
}
