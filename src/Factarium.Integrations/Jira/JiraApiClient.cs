using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Factarium.Integrations.Jira;

/// <summary>
/// Jira Cloud REST client. Uses the current issue-search endpoint
/// (<c>POST /rest/api/3/search/jql</c>, token-paged) plus the Agile board/sprint,
/// per-issue changelog, and (best-effort) development-status endpoints. Yields raw
/// JSON so payloads are stored close to source. Basic auth (email + API token),
/// waits out 429s.
/// </summary>
public sealed class JiraApiClient(HttpClient http, TimeProvider clock, ILogger<JiraApiClient> logger)
{
    private const int SearchPageSize = 100;
    private const int MaxRetries = 5;
    private const int MaxRateLimitWaitSeconds = 120;

    /// <summary>
    /// Resolves a site's cloud id from its unauthenticated tenant-info endpoint. Needed
    /// to route scoped-token requests through the Atlassian API gateway.
    /// </summary>
    public async Task<string?> ResolveCloudIdAsync(string siteUrl, CancellationToken cancellationToken)
    {
        var url = $"{siteUrl.TrimEnd('/')}/_edge/tenant_info";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Factarium");

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = await ParseAsync(response, cancellationToken);
        return doc.RootElement.TryGetProperty("cloudId", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
    }

    // Every method below prefixes its paths with <paramref name="apiRoot"/>: the site URL
    // for classic tokens, or https://api.atlassian.com/ex/jira/{cloudId} for scoped tokens.

    /// <summary>
    /// Pages the enhanced JQL search. The endpoint is cursor-paged via
    /// <c>nextPageToken</c> (there is no <c>total</c>/<c>startAt</c>) and requires an
    /// explicit field list. Yields each issue's raw JSON.
    /// </summary>
    public async IAsyncEnumerable<JsonElement> SearchIssuesAsync(
        string apiRoot,
        string jql,
        IReadOnlyList<string> fields,
        string email,
        string token,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var root = apiRoot.TrimEnd('/');
        var url = $"{root}/rest/api/3/search/jql";
        string? nextPageToken = null;

        while (true)
        {
            var payload = new Dictionary<string, object?>
            {
                ["jql"] = jql,
                ["fields"] = fields,
                ["maxResults"] = SearchPageSize,
            };
            if (nextPageToken is not null)
            {
                payload["nextPageToken"] = nextPageToken;
            }

            var bodyJson = JsonSerializer.Serialize(payload);
            using var response = await SendAsync(
                () => Json(HttpMethod.Post, url, bodyJson), email, token, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var doc = await ParseAsync(response, cancellationToken);
            var page = doc.RootElement;

            if (page.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
            {
                foreach (var issue in issues.EnumerateArray())
                {
                    yield return issue.Clone();
                }
            }

            nextPageToken = page.TryGetProperty("nextPageToken", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;

            if (nextPageToken is null)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// Resolves the custom-field ids for story points, sprint, and primary developer by
    /// name (they vary per instance). Returns nulls when a field isn't present.
    /// </summary>
    public async Task<(string? StoryPointsFieldId, string? SprintFieldId, string? PrimaryDeveloperFieldId)> DiscoverFieldIdsAsync(
        string apiRoot, string email, string token, CancellationToken cancellationToken)
    {
        var url = $"{apiRoot.TrimEnd('/')}/rest/api/3/field";
        using var response = await SendAsync(() => Get(url), email, token, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = await ParseAsync(response, cancellationToken);
        string? storyPoints = null;
        string? sprint = null;
        string? primaryDeveloper = null;

        foreach (var field in doc.RootElement.EnumerateArray())
        {
            var name = field.TryGetProperty("name", out var n) ? n.GetString() : null;
            var id = field.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (name is null || id is null)
            {
                continue;
            }

            // Team-managed projects call it "Story point estimate"; company-managed "Story Points".
            if (storyPoints is null
                && (name.Equals("Story point estimate", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Story Points", StringComparison.OrdinalIgnoreCase)))
            {
                storyPoints = id;
            }
            else if (sprint is null && name.Equals("Sprint", StringComparison.OrdinalIgnoreCase))
            {
                sprint = id;
            }
            else if (primaryDeveloper is null && name.Equals("Primary Developer", StringComparison.OrdinalIgnoreCase))
            {
                primaryDeveloper = id;
            }
        }

        return (storyPoints, sprint, primaryDeveloper);
    }

    /// <summary>Yields every changelog history entry for an issue (paged by startAt/total).</summary>
    public async IAsyncEnumerable<JsonElement> GetIssueChangelogAsync(
        string apiRoot,
        string issueId,
        string email,
        string token,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var root = apiRoot.TrimEnd('/');
        var startAt = 0;

        while (true)
        {
            var url = $"{root}/rest/api/3/issue/{issueId}/changelog?startAt={startAt}&maxResults={SearchPageSize}";
            using var response = await SendAsync(() => Get(url), email, token, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var doc = await ParseAsync(response, cancellationToken);
            var page = doc.RootElement;

            if (!page.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            var count = values.GetArrayLength();
            foreach (var entry in values.EnumerateArray())
            {
                yield return entry.Clone();
            }

            startAt += count;
            var isLast = page.TryGetProperty("isLast", out var last) && last.ValueKind == JsonValueKind.True;
            if (count == 0 || isLast)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// Development-panel links (pull requests and branches) for an issue, as a
    /// <c>{ issueId, pullRequests, branches }</c> payload, or null when the issue has
    /// no dev info. This is an internal, unsupported endpoint; it throws on transport
    /// errors so the caller can circuit-break (dev-links are best-effort).
    /// </summary>
    public async Task<JsonElement?> GetIssueDevLinksAsync(
        string apiRoot, string issueId, string email, string token, CancellationToken cancellationToken)
    {
        var root = apiRoot.TrimEnd('/');
        var pullRequests = await DevStatusAsync(root, issueId, "pullrequest", email, token, cancellationToken);
        var branches = await DevStatusAsync(root, issueId, "branch", email, token, cancellationToken);
        if (pullRequests.Count == 0 && branches.Count == 0)
        {
            return null;
        }

        var payload = new Dictionary<string, object?>
        {
            ["issueId"] = issueId,
            ["pullRequests"] = pullRequests,
            ["branches"] = branches,
        };
        return JsonSerializer.SerializeToElement(payload);
    }

    // Flattens the detail[].{pullRequests|branches} arrays for one dataType into a list of raw nodes.
    private async Task<List<JsonElement>> DevStatusAsync(
        string root, string issueId, string dataType, string email, string token, CancellationToken cancellationToken)
    {
        var url = $"{root}/rest/dev-status/1.0/issue/detail"
                  + $"?issueId={Uri.EscapeDataString(issueId)}&applicationType=GitHub&dataType={dataType}";

        using var response = await SendAsync(() => Get(url), email, token, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = await ParseAsync(response, cancellationToken);
        var results = new List<JsonElement>();

        if (doc.RootElement.TryGetProperty("detail", out var details) && details.ValueKind == JsonValueKind.Array)
        {
            var arrayName = dataType == "pullrequest" ? "pullRequests" : "branches";
            foreach (var detail in details.EnumerateArray())
            {
                if (detail.TryGetProperty(arrayName, out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var node in arr.EnumerateArray())
                    {
                        results.Add(node.Clone());
                    }
                }
            }
        }

        return results;
    }

    private static HttpRequestMessage Get(string url) => new(HttpMethod.Get, url);

    private static HttpRequestMessage Json(HttpMethod method, string url, string body) => new(method, url)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(body);
    }

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory, string email, string token, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = requestFactory();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("Factarium");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            var response = await http.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
            {
                var wait = RetryAfter(response);
                logger.LogWarning("Jira rate limit hit; waiting {Seconds}s", wait.TotalSeconds);
                response.Dispose();
                await Task.Delay(wait, clock, cancellationToken);
                continue;
            }

            return response;
        }
    }

    private TimeSpan RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values)
            && int.TryParse(values.FirstOrDefault(), out var seconds))
        {
            return TimeSpan.FromSeconds(Math.Clamp(seconds, 1, MaxRateLimitWaitSeconds));
        }

        return TimeSpan.FromSeconds(5);
    }
}
