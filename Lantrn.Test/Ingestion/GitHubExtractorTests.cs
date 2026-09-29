using System.IO.Compression;
using System.Net;
using System.Text;
using Lantrn.Services.Ingestion.Links;
using Lantrn.Test.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lantrn.Test.Ingestion;

public class GitHubExtractorTests
{
    private static readonly Dictionary<string, string> Repository = new()
    {
        ["repo-main/"] = "",
        ["repo-main/README.md"] = "# Repo",
        ["repo-main/src/app.cs"] = "class App { }",
        ["repo-main/src/logo.png"] = "not code",
        ["repo-main/src/data.cs"] = "binary\0data",
        ["repo-main/src/site.min.js"] = "minified",
        ["repo-main/src-old/legacy.cs"] = "class Legacy { }",
        ["repo-main/docs/guide.md"] = "# Guide",
        ["repo-main/node_modules/lib/index.js"] = "dependency",
        ["repo-main/.github/workflows/ci.yml"] = "hidden",
        ["repo-main/bin/Debug/out.cs"] = "build output",
    };

    [Theory]
    [InlineData("https://github.com/owner/repo")]
    [InlineData("https://github.com/owner/repo.git")]
    [InlineData("https://www.github.com/owner/repo/tree/main/src")]
    [InlineData("https://github.com/owner/repo/blob/main/README.md")]
    public void Takes_repository_and_folder_links(string url)
    {
        Assert.True(Extractor(Archive()).CanExtract(new Uri(url)));
    }

    [Theory]
    [InlineData("https://github.com/owner")]
    [InlineData("https://github.com/owner/repo/issues/1")]
    [InlineData("https://github.com/owner/repo/pulls")]
    [InlineData("https://gitlab.com/owner/repo")]
    [InlineData("https://github.com.evil.example/owner/repo")]
    public void Leaves_other_links_to_the_crawler(string url)
    {
        Assert.False(Extractor(Archive()).CanExtract(new Uri(url)));
    }

    [Fact]
    public async Task Keeps_the_code_and_docs_but_not_dependencies_build_output_or_binaries()
    {
        var handler = Archive();

        var files = await Extractor(handler).ExtractAsync(new Uri("https://github.com/owner/repo"));

        Assert.Equal("https://github.com/owner/repo/archive/HEAD.zip", Assert.Single(handler.Requests).RequestUri!.ToString());
        Assert.Equal(
            [
                "https://github.com/owner/repo/blob/HEAD/README.md",
                "https://github.com/owner/repo/blob/HEAD/docs/guide.md",
                "https://github.com/owner/repo/blob/HEAD/src-old/legacy.cs",
                "https://github.com/owner/repo/blob/HEAD/src/app.cs",
            ],
            files.Select(f => f.Source).Order(StringComparer.Ordinal));
        Assert.All(files, f => Assert.Equal(["repo"], f.Tags));
    }

    [Fact]
    public async Task A_folder_link_keeps_only_that_folder()
    {
        var handler = Archive();

        var files = await Extractor(handler).ExtractAsync(new Uri("https://github.com/owner/repo/tree/main/src"));

        Assert.Equal("https://github.com/owner/repo/archive/main.zip", Assert.Single(handler.Requests).RequestUri!.ToString());
        Assert.Equal("https://github.com/owner/repo/blob/main/src/app.cs", Assert.Single(files).Source);
    }

    [Fact]
    public async Task Code_is_fenced_under_its_path_and_handed_over_as_markdown()
    {
        var files = await Extractor(Archive()).ExtractAsync(new Uri("https://github.com/owner/repo/tree/main/src"));

        var file = Assert.Single(files);
        Assert.Equal("app.cs.md", file.FileName);
        Assert.Equal("# src/app.cs\n\n```csharp\nclass App { }\n```\n", Encoding.UTF8.GetString(file.Bytes));
    }

    [Fact]
    public async Task A_fence_in_the_code_cant_close_the_block()
    {
        var handler = Archive(new() { ["repo-main/docs.cs"] = "var md = \"```\\ncode\\n```\";" });

        var file = Assert.Single(await Extractor(handler).ExtractAsync(new Uri("https://github.com/owner/repo")));

        var markdown = Encoding.UTF8.GetString(file.Bytes);
        Assert.StartsWith("# docs.cs\n\n````csharp\n", markdown);
        Assert.EndsWith("\n````\n", markdown);
    }

    [Fact]
    public async Task A_missing_repository_says_only_public_ones_can_be_added()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Extractor(handler).ExtractAsync(new Uri("https://github.com/owner/private-repo")));

        Assert.Contains("Only public repositories", error.Message);
    }

    private static GitHubExtractor Extractor(StubHttpHandler handler) =>
        new(new HttpClient(handler), NullLogger<GitHubExtractor>.Instance);

    private static StubHttpHandler Archive(Dictionary<string, string>? entries = null)
    {
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries ?? Repository)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        var bytes = zip.ToArray();
        return new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
}
