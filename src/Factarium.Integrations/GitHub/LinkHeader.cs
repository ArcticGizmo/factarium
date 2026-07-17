using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Factarium.Integrations.GitHub;

/// <summary>
/// Parses GitHub's RFC 5988 <c>Link</c> pagination header, e.g.
/// <c>&lt;https://api.github.com/...&amp;page=2&gt;; rel="next", &lt;...&gt;; rel="last"</c>.
/// </summary>
internal static partial class LinkHeader
{
    [GeneratedRegex("<(?<url>[^>]+)>;\\s*rel=\"(?<rel>[^\"]+)\"")]
    private static partial Regex LinkEntry();

    public static string? NextUrl(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        foreach (var header in values)
        {
            foreach (Match match in LinkEntry().Matches(header))
            {
                if (match.Groups["rel"].Value == "next")
                {
                    return match.Groups["url"].Value;
                }
            }
        }

        return null;
    }
}
