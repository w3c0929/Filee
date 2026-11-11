// GitHub release downloads are often slow or blocked. For GitHub URLs, try a set of public proxies first
// (each takes the original address after a slash), then the original. The pinned SHA-256 still decides.

namespace Filee.Engines.Infrastructure;

internal static class DownloadMirrors
{
    /// <summary>Proxies tried in order, slowest last; the original URL always follows them.</summary>
    private static readonly string[] Prefixes =
    [
        "https://gh-proxy.org",
        "https://hk.gh-proxy.org",
        "https://cdn.gh-proxy.com",
        "https://git.yylx.win",
        "https://gh.monlor.com",
        "https://gh.my-website.ccwu.cc",
        "https://tvv.tw",
        "https://ghfast.top",
        "https://gh.acmsz.top",
        "https://github.ednovas.xyz",
        "https://gh.sixyin.com",
        "https://ghproxy.imciel.com",
        "https://cdn.akaere.online",
        "https://gh-proxy.com",
        "https://free.cn.eu.org",
        "https://fastgit.cc",
        "https://github.mxw.qzz.io",
        "https://xsadwsd.kdns.fr",
        "https://ghproxy.felicity.land",
        "https://down.mxw.qzz.io",
        "https://gh.07150721.xyz",
        "https://github.tbap.top",
        "https://gh.noki.icu",
        "https://ghproxy.net",
    ];

    /// <summary>The mirrors to try for <paramref name="url"/>, then the URL itself.</summary>
    public static IEnumerable<Uri> Candidates(Uri url)
    {
        if (IsGitHub(url))
            foreach (var prefix in Prefixes)
                yield return new Uri(prefix.TrimEnd('/') + "/" + url.AbsoluteUri);
        yield return url;
    }

    private static bool IsGitHub(Uri url) =>
        url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        || url.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
        || url.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
}
