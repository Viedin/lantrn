using System.Net;
using System.Text;
using Lantrn.Services.Ingestion;
using Lantrn.Test.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lantrn.Test.Ingestion;

public class WebCrawlerTests
{
    // Anyone who can add a website picks the URL, so it must never reach this machine, its network or cloud metadata.
    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://localhost/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://100.64.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    public async Task Refuses_private_and_local_addresses(string url)
    {
        using var http = new HttpClient(WebCrawler.CreateHandler());

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(url));

        Assert.Contains("private or local address", error.ToString());
    }

    [Fact]
    public async Task Refuses_a_redirect_to_another_host()
    {
        var crawler = Crawler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html></html>", Encoding.UTF8, "text/html"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://elsewhere.example/"),
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => crawler.FetchAsync(new Uri("https://docs.example/start")));
    }

    [Fact]
    public async Task Refuses_content_it_cant_read()
    {
        var crawler = Crawler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new("image/png") } },
        });

        await Assert.ThrowsAsync<NotSupportedException>(() => crawler.FetchAsync(new Uri("https://docs.example/logo")));
    }

    [Fact]
    public async Task Keeps_the_main_content_in_the_pages_own_charset()
    {
        var html = "<html><body><nav>Site menu</nav><main><h1>Café</h1></main><footer>Cookies</footer></body></html>";
        var crawler = Crawler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.Latin1.GetBytes(html)) { Headers = { ContentType = new("text/html") { CharSet = "iso-8859-1" } } },
        });

        var page = await crawler.FetchAsync(new Uri("https://docs.example/docs/quickstart/"));

        var text = Encoding.UTF8.GetString(page.Bytes);
        Assert.Contains("<h1>Café</h1>", text);
        Assert.DoesNotContain("Site menu", text);
        Assert.DoesNotContain("Cookies", text);
        Assert.Equal("docs-quickstart.html", page.FileName);
    }

    private static WebCrawler Crawler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHttpHandler(respond)), NullLogger<WebCrawler>.Instance);
}
