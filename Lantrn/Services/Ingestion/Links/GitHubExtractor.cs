using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Lantrn.Services.Ingestion.Links;

// "https://github.com/owner/repo", or ".../tree/main/src" for one folder: the code, READMEs and docs in it, downloaded
// as one archive. Each file is stored as markdown under its GitHub URL, so search hits link back to it.
public sealed class GitHubExtractor(HttpClient http, ILogger<GitHubExtractor> logger) : ILinkExtractor
{
    private const long MaxArchiveBytes = 100 * 1024 * 1024;
    private const int MaxFiles = 1000;
    private const int MaxFileBytes = 512 * 1024;

    // Dependencies and build output rather than the repository's own code.
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "vendor", "bin", "obj", "dist", "build", "out", "target", "packages", "__pycache__", "venv",
    };

    private static readonly HashSet<string> DocsExtensions = new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown", ".mdx" };

    // Extension to the code block language.
    private static readonly Dictionary<string, string> CodeLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "csharp", [".fs"] = "fsharp", [".vb"] = "vb", [".razor"] = "razor", [".cshtml"] = "cshtml",
        [".py"] = "python", [".rb"] = "ruby", [".php"] = "php", [".lua"] = "lua", [".pl"] = "perl",
        [".js"] = "javascript", [".mjs"] = "javascript", [".cjs"] = "javascript", [".jsx"] = "jsx",
        [".ts"] = "typescript", [".tsx"] = "tsx", [".vue"] = "vue", [".svelte"] = "svelte",
        [".java"] = "java", [".kt"] = "kotlin", [".kts"] = "kotlin", [".scala"] = "scala", [".groovy"] = "groovy",
        [".go"] = "go", [".rs"] = "rust", [".zig"] = "zig", [".swift"] = "swift", [".dart"] = "dart",
        [".c"] = "c", [".h"] = "c", [".cpp"] = "cpp", [".cc"] = "cpp", [".hpp"] = "cpp", [".m"] = "objectivec",
        [".ex"] = "elixir", [".exs"] = "elixir", [".erl"] = "erlang", [".hs"] = "haskell", [".clj"] = "clojure",
        [".ml"] = "ocaml", [".r"] = "r", [".jl"] = "julia",
        [".sh"] = "bash", [".bash"] = "bash", [".ps1"] = "powershell", [".sql"] = "sql",
        [".html"] = "html", [".css"] = "css", [".scss"] = "scss", [".proto"] = "protobuf", [".graphql"] = "graphql",
    };

    public string Label => "GitHub repository";

    public bool CanExtract(Uri url) => Parse(url) is not null;

    public async Task<IReadOnlyList<ExtractedFile>> ExtractAsync(Uri url, CancellationToken cancellationToken = default)
    {
        var repository = Parse(url) ?? throw new NotSupportedException($"{url} is not a GitHub repository.");
        using var archive = new ZipArchive(await DownloadAsync(repository, cancellationToken));
        var tags = DocumentStore.ParseTags(repository.Name);

        var files = new List<ExtractedFile>();
        foreach (var entry in archive.Entries)
        {
            // "lantrn-main/src/app.cs" -> "src/app.cs"
            var path = entry.FullName[(entry.FullName.IndexOf('/') + 1)..];
            if (entry.Length is 0 or > MaxFileBytes || !repository.Contains(path) || IsSkipped(path) || ToFileName(path) is not { } fileName)
            {
                continue;
            }

            using var reader = new StreamReader(entry.Open());
            var text = await reader.ReadToEndAsync(cancellationToken);
            if (text.Contains('\0'))
            {
                continue;
            }

            if (files.Count == MaxFiles)
            {
                throw new InvalidOperationException(
                    $"{repository} has more than {MaxFiles} code and docs files. Add one folder at a time with a link like https://github.com/{repository.Owner}/{repository.Name}/tree/{repository.Ref}/src.");
            }

            files.Add(new ExtractedFile(repository.FileUrl(path), fileName, Encoding.UTF8.GetBytes(ToMarkdown(path, text)), tags));
        }

        logger.LogInformation("Extracted {Files} files from {Repository}", files.Count, repository);
        return files.Count > 0 ? files : throw new InvalidOperationException($"Found no code or docs in {repository}.");
    }

    private async Task<MemoryStream> DownloadAsync(Repository repository, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(repository.ArchiveUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"Found no {repository} on GitHub. Only public repositories can be added.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GitHub answered HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var buffer = new MemoryStream();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        await IngestQueue.CopyLimitedAsync(content, buffer, MaxArchiveBytes, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }

    // "https://github.com/owner/repo", ".../tree/main/src" or ".../blob/main/README.md"; anything else, such as an
    // issue or a branch name with a slash in it, is left to the other extractors.
    private static Repository? Parse(Uri url)
    {
        if (url.Host is not ("github.com" or "www.github.com"))
        {
            return null;
        }

        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (segments.Length < 2 || !IsName(segments[0]) || !IsName(segments[1]))
        {
            return null;
        }

        var name = Regex.Replace(segments[1], @"\.git$", string.Empty, RegexOptions.IgnoreCase);
        return segments switch
        {
            [var owner, _] => new Repository(owner, name, "HEAD", string.Empty),
            [var owner, _, "tree" or "blob", var reference, .. var folder] => new Repository(owner, name, reference, string.Join('/', folder)),
            _ => null,
        };
    }

    private static bool IsName(string segment) =>
        segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    private static bool IsSkipped(string path)
    {
        var segments = path.Split('/');
        return segments.Any(s => s.StartsWith('.'))
               || segments[..^1].Any(SkippedFolders.Contains)
               || segments[^1].Contains(".min.", StringComparison.OrdinalIgnoreCase);
    }

    // Xberg picks the parser from the extension, so everything is handed over as markdown:
    // "src/app.cs" -> "app.cs.md", "README.md" as it is, and null for anything that isn't code or docs.
    private static string? ToFileName(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(name);
        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        var isDocs = DocsExtensions.Contains(extension) || Path.GetFileNameWithoutExtension(name).Equals("README", StringComparison.OrdinalIgnoreCase);
        return isDocs || CodeLanguages.ContainsKey(extension) ? name + ".md" : null;
    }

    // Code goes in a code block under its path, so each chunk's section says which file it came from.
    private static string ToMarkdown(string path, string text)
    {
        if (!CodeLanguages.TryGetValue(Path.GetExtension(path), out var language))
        {
            return text;
        }

        // Longer than any run of backticks in the code, so a markdown string inside it can't close the block.
        var longestRun = Regex.Matches(text, "`+").Select(m => m.Length).DefaultIfEmpty(0).Max();
        var fence = new string('`', Math.Max(3, longestRun + 1));
        return $"# {path}\n\n{fence}{language}\n{text.TrimEnd()}\n{fence}\n";
    }

    private sealed record Repository(string Owner, string Name, string Ref, string Folder)
    {
        public Uri ArchiveUrl => new($"https://github.com/{Owner}/{Name}/archive/{Uri.EscapeDataString(Ref)}.zip");

        public string FileUrl(string path) =>
            $"https://github.com/{Owner}/{Name}/blob/{Uri.EscapeDataString(Ref)}/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}";

        public bool Contains(string path) =>
            Folder.Length == 0 || path == Folder || path.StartsWith(Folder + "/", StringComparison.Ordinal);

        public override string ToString() => Folder.Length == 0 ? $"{Owner}/{Name}" : $"{Owner}/{Name}/{Folder}";
    }
}
