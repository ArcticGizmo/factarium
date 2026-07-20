using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Factarium.Integrations.Tempo;

/// <summary>
/// Tempo Cloud REST client (<c>https://api.tempo.io/4</c>). Pages the worklogs endpoint via
/// offset/limit, following the <c>metadata.next</c> link, and yields raw worklog JSON. Bearer
/// auth with a Tempo API token; waits out 429s.
/// </summary>
public sealed class TempoApiClient(HttpClient http, TimeProvider clock, ILogger<TempoApiClient> logger)
{
    private const int PageSize = 1000;
    private const int MaxRetries = 5;
    private const int MaxRateLimitWaitSeconds = 120;

    /// <summary>
    /// Yields every worklog for a project from <paramref name="from"/> onward (all projects
    /// when <paramref name="projectKey"/> is null). Cursor-paged via <c>metadata.next</c>.
    /// </summary>
    public async IAsyncEnumerable<JsonElement> GetWorklogsAsync(
        string? projectKey, DateOnly? from, string token, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = new List<string> { $"limit={PageSize}", "offset=0" };
        if (!string.IsNullOrWhiteSpace(projectKey))
        {
            query.Add($"project={Uri.EscapeDataString(projectKey)}");
        }

        // Tempo's date filter is a closed range: a "from" without a "to" is rejected (400),
        // so pair it with today's date whenever a history floor is set.
        if (from is not null)
        {
            var to = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            query.Add($"from={from.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
            query.Add($"to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }

        // The first request is relative to the client's BaseAddress; metadata.next is absolute.
        string? url = $"4/worklogs?{string.Join("&", query)}";

        while (url is not null)
        {
            using var response = await SendAsync(url, token, cancellationToken);
            await EnsureSuccessAsync(response, url, cancellationToken);

            using var doc = await ParseAsync(response, cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var worklog in results.EnumerateArray())
                {
                    yield return worklog.Clone();
                }
            }

            url = root.TryGetProperty("metadata", out var meta)
                  && meta.TryGetProperty("next", out var next)
                  && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }
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

    private async Task<HttpResponseMessage> SendAsync(string url, string token, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
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
