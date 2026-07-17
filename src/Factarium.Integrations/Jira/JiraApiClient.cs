using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Factarium.Integrations.Jira;

/// <summary>
/// Minimal Jira Cloud REST client: pages the JQL search endpoint (startAt/total)
/// with changelog expanded, yielding each issue's raw JSON. Uses Basic auth
/// (email + API token) and waits out 429s.
/// </summary>
public sealed class JiraApiClient(HttpClient http, TimeProvider clock, ILogger<JiraApiClient> logger)
{
    private const int PageSize = 100;
    private const int MaxRetries = 5;
    private const int MaxRateLimitWaitSeconds = 120;

    private const string Fields = "summary,status,assignee,created,updated,resolutiondate,issuetype,project";

    public async IAsyncEnumerable<JsonElement> SearchIssuesAsync(
        string baseUrl,
        string jql,
        string email,
        string token,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var root = baseUrl.TrimEnd('/');
        var startAt = 0;
        var total = int.MaxValue;

        while (startAt < total)
        {
            var url = $"{root}/rest/api/3/search"
                      + $"?jql={Uri.EscapeDataString(jql)}"
                      + $"&startAt={startAt}&maxResults={PageSize}"
                      + "&expand=changelog"
                      + $"&fields={Fields}";

            using var response = await SendAsync(url, email, token, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var page = doc.RootElement;

            total = page.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
            if (!page.TryGetProperty("issues", out var issues) || issues.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            var count = issues.GetArrayLength();
            if (count == 0)
            {
                yield break;
            }

            foreach (var issue in issues.EnumerateArray())
            {
                yield return issue.Clone();
            }

            startAt += count;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string url, string email, string token, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
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
