using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Factarium.Integrations.GitHub;

/// <summary>
/// Thin GitHub REST client that yields raw JSON elements (so payloads are stored
/// exactly as GitHub sends them), follows Link-header pagination, and waits out
/// primary/secondary rate limits.
/// </summary>
public sealed class GitHubApiClient(HttpClient http, TimeProvider clock, ILogger<GitHubApiClient> logger)
{
    private const int MaxRateLimitWaitSeconds = 300;
    private const int MaxRetries = 5;

    /// <summary>Enumerates every item across all pages of a list endpoint.</summary>
    public async IAsyncEnumerable<JsonElement> GetPagedAsync(
        string url,
        string? token,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var next = Absolute(url);
        while (next is not null)
        {
            using var response = await SendAsync(next, token, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                yield return item.Clone();
            }

            next = LinkHeader.NextUrl(response.Headers);
        }
    }

    /// <summary>Fetches a single object, or null on 404.</summary>
    public async Task<JsonElement?> GetObjectAsync(string url, string? token, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(Absolute(url), token, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private async Task<HttpResponseMessage> SendAsync(string url, string? token, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("Factarium");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var response = await http.SendAsync(request, cancellationToken);

            if (IsRateLimited(response) && attempt < MaxRetries)
            {
                var wait = RateLimitDelay(response);
                logger.LogWarning("GitHub rate limit hit; waiting {Seconds}s before retry", wait.TotalSeconds);
                response.Dispose();
                await Task.Delay(wait, clock, cancellationToken);
                continue;
            }

            return response;
        }
    }

    private static bool IsRateLimited(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        // Primary limit: 403 with the remaining counter at zero.
        return response.StatusCode == HttpStatusCode.Forbidden
               && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
               && remaining.FirstOrDefault() == "0";
    }

    private TimeSpan RateLimitDelay(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var retryAfter)
            && int.TryParse(retryAfter.FirstOrDefault(), out var seconds))
        {
            return TimeSpan.FromSeconds(Math.Clamp(seconds, 1, MaxRateLimitWaitSeconds));
        }

        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var reset)
            && long.TryParse(reset.FirstOrDefault(), out var resetUnix))
        {
            var delta = DateTimeOffset.FromUnixTimeSeconds(resetUnix) - clock.GetUtcNow();
            var secondsUntilReset = (int)Math.Ceiling(delta.TotalSeconds);
            return TimeSpan.FromSeconds(Math.Clamp(secondsUntilReset, 1, MaxRateLimitWaitSeconds));
        }

        return TimeSpan.FromSeconds(5);
    }

    private static string Absolute(string url) =>
        url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : url.TrimStart('/');
}
