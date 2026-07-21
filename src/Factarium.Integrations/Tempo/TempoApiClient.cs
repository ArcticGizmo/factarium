using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Factarium.Integrations.Tempo;

/// <summary>
/// Tempo Cloud REST client (<c>https://api.tempo.io/4</c>). Pulls worklogs through the v4
/// <c>POST /worklogs/search</c> endpoint, which scopes the query to specific projects via a
/// <c>projectIds</c> body filter (rather than pulling the whole organisation) and pages by
/// offset/limit. Bearer auth with a Tempo API token; waits out 429s.
/// </summary>
public sealed class TempoApiClient(HttpClient http, TimeProvider clock, ILogger<TempoApiClient> logger)
{
    private const int PageSize = 1000;
    private const int MaxRetries = 5;
    private const int MaxRateLimitWaitSeconds = 120;

    // /worklogs/search requires a closed from/to range. When a connection sets no history floor,
    // start from a date comfortably before any real Jira worklog so "everything" still bounds.
    private static readonly DateOnly DefaultFrom = new(2000, 1, 1);

    /// <summary>
    /// Yields every worklog for a project from <paramref name="from"/> onward (all projects when
    /// <paramref name="projectId"/> is null). Scoped server-side via the search endpoint's
    /// <c>projectIds</c> filter and paged by offset/limit.
    /// </summary>
    public async IAsyncEnumerable<JsonElement> GetWorklogsAsync(
        string? projectId, DateOnly? from, string token, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Tempo v4 scopes by numeric project id, so a project key is rejected up front with a
        // message pointing at the fix rather than an opaque 400 from the API.
        var projectIds = ParseProjectIds(projectId);
        var fromDate = from ?? DefaultFrom;
        var toDate = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var body = BuildSearchBody(fromDate, toDate, projectIds);

        // The search endpoint doesn't hand back a followable GET link, so walk offset/limit
        // ourselves until a page comes back short (fewer than a full page of results).
        for (var offset = 0; ; offset += PageSize)
        {
            var url = $"4/worklogs/search?limit={PageSize}&offset={offset}";
            using var response = await SendAsync(
                () => new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                },
                token,
                cancellationToken);
            await EnsureSuccessAsync(response, url, cancellationToken);

            using var doc = await ParseAsync(response, cancellationToken);

            var pageCount = 0;
            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var worklog in results.EnumerateArray())
                {
                    pageCount++;
                    yield return worklog.Clone();
                }
            }

            if (pageCount < PageSize)
            {
                yield break;
            }
        }
    }

    // A single-element projectIds filter (or empty for all projects). v4 keys worklog search on
    // the numeric Jira project id; the project key that older Tempo APIs accepted no longer works.
    private static IReadOnlyList<long> ParseProjectIds(string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return [];
        }

        if (!long.TryParse(projectId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            throw new InvalidOperationException(
                $"Tempo project id must be numeric (got '{projectId}'). Use the numeric Jira project id, not the project key.");
        }

        return [id];
    }

    private static string BuildSearchBody(DateOnly from, DateOnly to, IReadOnlyList<long> projectIds)
    {
        var node = new JsonObject
        {
            ["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

        if (projectIds.Count > 0)
        {
            var ids = new JsonArray();
            foreach (var id in projectIds)
            {
                ids.Add(id);
            }

            node["projectIds"] = ids;
        }

        return node.ToJsonString();
    }

    // Surfaces Tempo's error body (the 4xx JSON explains exactly what it rejected) instead of
    // the opaque message EnsureSuccessStatusCode would throw.
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string url, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 500)
        {
            body = body[..500];
        }

        throw new HttpRequestException(
            $"Tempo worklogs request failed ({(int)response.StatusCode} {response.StatusCode}) for '{url}': {body}");
    }

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(body);
    }

    // Takes a factory (not a prebuilt request) because a request — and its POST body content —
    // can only be sent once, so each 429 retry needs a fresh instance.
    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory, string token, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = requestFactory();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("Factarium");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await http.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
            {
                var wait = RetryAfter(response);
                logger.LogWarning("Tempo rate limit hit; waiting {Seconds}s", wait.TotalSeconds);
                response.Dispose();
                await Task.Delay(wait, clock, cancellationToken);
                continue;
            }

            return response;
        }
    }

    private static TimeSpan RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values)
            && int.TryParse(values.FirstOrDefault(), out var seconds))
        {
            return TimeSpan.FromSeconds(Math.Clamp(seconds, 1, MaxRateLimitWaitSeconds));
        }

        return TimeSpan.FromSeconds(5);
    }
}
