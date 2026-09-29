using Lantrn.Services.Search;

namespace Lantrn.Test.Search;

public class SearchAssistantTests
{
    [Theory]
    [InlineData("The answer [1].", "The answer [1].")]
    [InlineData("\n\n  The answer [1].", "The answer [1].")]
    [InlineData("<think>The user wants X.</think>\n\nThe answer [1].", "The answer [1].")]
    [InlineData("<think>Still reasoning about the", "")]
    public void Only_the_answer_after_any_reasoning_is_shown(string streamed, string visible)
    {
        Assert.Equal(visible, SearchAssistant.VisibleAnswer(streamed));
    }
}
