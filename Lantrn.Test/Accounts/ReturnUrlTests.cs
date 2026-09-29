using Lantrn.Components.Account;

namespace Lantrn.Test.Accounts;

// The login page redirects to this after signing in, so anything off-site would be an open redirect.
public class ReturnUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/search?q=invoice")]
    [InlineData("/collections/3f2a#documents")]
    public void Keeps_paths_on_this_site(string url)
    {
        Assert.Equal(url, ReturnUrl.Safe(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("search")]
    public void Sends_anything_else_home(string? url)
    {
        Assert.Equal("/", ReturnUrl.Safe(url));
    }
}
