using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Xberg;

namespace Lantrn.Services;

/// <summary>
/// Finds the pages under one path on one host through Xberg's bundled crawler, which reads the sitemap,
/// follows links, honours robots.txt and refuses private network addresses. Pages are then fetched here
/// rather than by Xberg, because Xberg's URL path ignores the HTML options and keeps all the site chrome.
/// </summary>
public sealed class WebCrawler(HttpClient http, ILogger<WebCrawler> logger)
{
    // Some sites refuse requests without a User-Agent.
    public const string UserAgent = "Mozilla/5.0 (compatible; Lantrn/1.0)";

    public const long MaxPageBytes = 50 * 1024 * 1024;

    // Extension to hand Xberg by response type, since it picks the parser from the file name.
    private static readonly Dictionary<string, string> FetchedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["text/html"] = ".html",
        ["application/xhtml+xml"] = ".html",
        ["application/pdf"] = ".pdf",
        ["text/markdown"] = ".md",
        ["text/plain"] = ".txt",
    };

    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico", ".css", ".js", ".mjs", ".json", ".xml",
        ".zip", ".gz", ".mp4", ".webm", ".mp3", ".woff", ".woff2", ".ttf",
    };

    // Checks addresses at connect time rather than per URL, so neither the start URL, a redirect
    // nor a DNS answer that changes between check and fetch can reach this machine or its network.
    public static SocketsHttpHandler CreateHandler() => new()
    {
        ConnectCallback = async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            if (addresses.Length == 0 || !addresses.All(IsPublic))
            {
                throw new HttpRequestException($"Refusing to fetch {host}: it is a private or local address.");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    // "https://openrouter.ai/docs/quickstart" -> "/docs"
    public static string DefaultScope(Uri url)
    {
        var first = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is null ? "/" : "/" + first;
    }

    public async Task<IReadOnlyList<Uri>> MapAsync(Uri start, string scope, int maxPages, CancellationToken cancellationToken = default)
    {
        scope = NormalizeScope(scope);
        logger.LogInformation("Mapping {Start} within {Host}{Scope}, up to {MaxPages} pages", start, start.Host, scope, maxPages);

        // The binding takes no cancellation token, so Stop only stops waiting for the map.
        var map = await XbergConverter.MapUrlAsync(
                start.AbsoluteUri,
                new UrlExtractionConfig { Crawl = CrawlConfig(scope, maxPages) })
            .WaitAsync(cancellationToken);

        // The start page is always crawled; the scope only filters what is discovered from it.
        var seen = new HashSet<string>(StringComparer.Ordinal) { Normalize(start).AbsoluteUri };
        var pages = new List<Uri> { Normalize(start) };
        var dropped = new List<string>();
        foreach (var entry in map.Urls)
        {
            if (pages.Count >= maxPages)
            {
                break;
            }

            if (Uri.TryCreate(entry.Url, System.UriKind.Absolute, out var url)
                && Normalize(url) is var normalized
                && InScope(normalized, start, scope))
            {
                if (seen.Add(normalized.AbsoluteUri))
                {
                    pages.Add(normalized);
                }
            }
            else if (dropped.Count < 10)
            {
                dropped.Add(entry.Url);
            }
        }

        if (dropped.Count > 0)
        {
            logger.LogDebug("Dropped out-of-scope URLs from the map of {Start}, such as: {Urls}", start, dropped);
        }

        logger.LogInformation("Map of {Start} found {Found} URLs, keeping {Pages}", start, map.Urls.Count, pages.Count);
        return pages;
    }

    private static CrawlConfig CrawlConfig(string scope, int maxPages) => new()
    {
        StayOnDomain = true,
        IncludePaths = scope == "/" ? [] : [$"^{Regex.Escape(scope)}(/|$)"],
        // IncludePaths alone did not scope the map: MapLimit filled up with the rest of the site
        // before the pages under the scope, so filter the map results themselves.
        MapSearch = scope == "/" ? null : scope,
        // Headroom for the asset URLs dropped by InScope.
        MapLimit = (ulong)maxPages * 2,
        RespectRobotsTxt = true,
        UserAgent = UserAgent,
        RequestTimeout = 30_000,
    };

    public async Task<FetchedPage> FetchAsync(Uri url, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        // The map only vouched for this host; a redirect elsewhere could reach anything.
        var finalUrl = response.RequestMessage?.RequestUri ?? url;
        if (!finalUrl.Host.Equals(url.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Redirected off-site to {finalUrl}.");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!FetchedTypes.TryGetValue(mediaType, out var extension))
        {
            throw new NotSupportedException($"Unsupported content type '{mediaType}'.");
        }

        // HTML is read as a string so the page's declared charset is honoured, then handed on as UTF-8.
        var bytes = extension == ".html"
            ? Encoding.UTF8.GetBytes(TrimToMainContent(await response.Content.ReadAsStringAsync(cancellationToken)))
            : await response.Content.ReadAsByteArrayAsync(cancellationToken);

        // Xberg picks the parser from the extension, so give it "docs-quickstart.html".
        var slug = finalUrl.AbsolutePath.Trim('/').Replace('/', '-');
        slug = slug.Length == 0 ? "index" : slug;
        var fileName = slug.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? slug : slug + extension;
        return new FetchedPage(finalUrl, bytes, fileName);
    }

    // Most sites wrap the page body in <main> or <article>; cutting to it before Xberg's own
    // nav/footer stripping gets rid of site chrome that isn't marked up semantically.
    private static string TrimToMainContent(string html)
    {
        foreach (var tag in (string[])["main", "article", "body"])
        {
            var open = Regex.Match(html, $@"<{tag}[\s>]", RegexOptions.IgnoreCase);
            var close = html.LastIndexOf($"</{tag}>", StringComparison.OrdinalIgnoreCase);
            if (open.Success && close > open.Index)
            {
                return $"<!DOCTYPE html><html><body>{html[open.Index..(close + tag.Length + 3)]}</body></html>";
            }
        }

        return html;
    }

    private static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal);
        }

        // 0/8, 10/8, 100.64/10 (carrier-grade NAT), 169.254/16 (link-local, cloud metadata), 172.16/12, 192.168/16.
        var b = address.GetAddressBytes();
        return !(b[0] is 0 or 10
                 || (b[0] == 100 && b[1] is >= 64 and < 128)
                 || (b[0] == 169 && b[1] == 254)
                 || (b[0] == 172 && b[1] is >= 16 and < 32)
                 || (b[0] == 192 && b[1] == 168));
    }

    private static Uri Normalize(Uri url)
    {
        var builder = new UriBuilder(url) { Fragment = string.Empty, Query = string.Empty };
        if (builder.Path.Length > 1)
        {
            builder.Path = builder.Path.TrimEnd('/');
        }
        return builder.Uri;
    }

    // " docs/ " -> "/docs", "" -> "/"
    private static string NormalizeScope(string scope) => "/" + scope.Trim().Trim('/');

    // The map already applies the scope; this guards against it returning assets or other hosts.
    private static bool InScope(Uri url, Uri start, string scope)
    {
        if (url.Scheme is not ("http" or "https") || !url.Host.Equals(start.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = url.AbsolutePath;
        var underScope = scope == "/"
                         || path.Equals(scope, StringComparison.OrdinalIgnoreCase)
                         || path.StartsWith(scope + "/", StringComparison.OrdinalIgnoreCase);

        return underScope && !SkippedExtensions.Contains(Path.GetExtension(path));
    }
}

public sealed record FetchedPage(Uri Url, byte[] Bytes, string FileName);
