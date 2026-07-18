using System.Globalization;
using System.Text.Json;
using Factarium.Application.Sync;
using Microsoft.Extensions.Logging;

namespace Factarium.Integrations.Jira;

/// <summary>
/// Pull source for Jira Cloud. Requires a site URL and account email
/// (<see cref="JiraSourceConfig"/>) plus an API token credential; optional project
/// keys or raw JQL. Syncs issues (with changelog) into the bronze tier, advancing an
/// updated-time cursor.
/// </summary>
public sealed class JiraPullSource(JiraApiClient client, ILogger<JiraPullSource> logger) : IPullSource
{
    private const string Source = "jira";
    private const string CursorKey = "issues:updated";

    public string Type => Source;

    public async Task<SyncResult> PullAsync(SyncContext context, CancellationToken cancellationToken)
    {
        if (context.Config is not JiraSourceConfig config)
        {
            return SyncResult.Failed("Jira integration is misconfigured (expected Jira configuration).");
        }

        if (string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(config.Email))
        {
            return SyncResult.Failed("Jira integration requires a site URL and an account email.");
        }

        if (string.IsNullOrWhiteSpace(context.Credential))
        {
            return SyncResult.Failed("Jira integration requires an API token credential.");
        }

        var baseUrl = config.BaseUrl;
        var email = config.Email;
        var lastCursor = ParseDate(context.Cursor.Get(CursorKey));
        var jql = BuildJql(config, lastCursor);

        var facts = new List<RawFact>();
        DateTimeOffset? maxUpdated = lastCursor;

        await foreach (var issue in client.SearchIssuesAsync(baseUrl, jql, email, context.Credential, cancellationToken))
        {
            var id = issue.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
            if (id is null)
            {
                continue;
            }

            var updated = issue.TryGetProperty("fields", out var fields) ? ParseDateElement(fields, "updated") : null;
            facts.Add(new RawFact(Source, "issue", id, issue.GetRawText(), updated));

            if (updated is not null && (maxUpdated is null || updated > maxUpdated))
            {
                maxUpdated = updated;
            }
        }

        var written = await context.Sink.WriteAsync(context.IntegrationId, facts, cancellationToken);
        if (maxUpdated is not null && maxUpdated != lastCursor)
        {
            context.Cursor.Set(CursorKey, maxUpdated.Value.ToUniversalTime().ToString("o"));
        }

        logger.LogInformation("Jira sync wrote {Count} issue records", written);
        return SyncResult.Ok(written);
    }

    private static string BuildJql(JiraSourceConfig config, DateTimeOffset? cursor)
    {
        var clauses = new List<string>();

        if (!string.IsNullOrWhiteSpace(config.Jql))
        {
            clauses.Add($"({config.Jql})");
        }
        else
        {
            var keys = config.ProjectKeys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .ToArray();
            if (keys.Length > 0)
            {
                clauses.Add($"project in ({string.Join(',', keys.Select(k => $"\"{k}\""))})");
            }
        }

        if (cursor is not null)
        {
            clauses.Add($"updated >= \"{cursor.Value.ToUniversalTime():yyyy-MM-dd HH:mm}\"");
        }

        var where = string.Join(" AND ", clauses);
        return where.Length > 0 ? $"{where} ORDER BY updated ASC" : "ORDER BY updated ASC";
    }

    private static DateTimeOffset? ParseDateElement(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? ParseDate(value.GetString())
            : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}
