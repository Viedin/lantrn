namespace Lantrn.Components.Account;

public static class ReturnUrl
{
    // Only same-site paths: "//host" and "/\host" are protocol-relative and would leave the app.
    public static string Safe(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
            ? url
            : "/";
}
