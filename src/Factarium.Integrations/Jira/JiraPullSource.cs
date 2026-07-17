using System.Globalization;
using System.Text.Json;
using Factarium.Application.Sync;
using Microsoft.Extensions.Logging;

namespace Factarium.Integrations.Jira;

/// <summary>
/// Pull source for Jira Cloud. Requires <c>baseUrl</c> and <c>email</c> settings
/// and an API token credential; optional <c>projectKeys</c> (csv) or <c>jql</c>.
/// Syncs issues (with changelog) into the bronze tier, advancing an updated-time
/// cursor.
/// </summary>
public sealed class JiraPullSource(JiraApiClient client, ILogger<JiraPullSource> logger) : IPullSource
{
    private const string Source = "jira";
    private const string CursorKey = "issues:updated";

    public string Type => Source;

    public async Task<SyncResult> PullAsync(SyncContext context, CancellationToken cancellationToken)
    {
        if (!TryGet(context, "baseUrl", out var baseUrl) || !TryGet(context, "email", out var email))
        {
            return SyncResult.Failed("Jira integration requires 'baseUrl' and 'email' settings.");
        }

        if (string.IsNullOrWhiteSpace(context.Credential))
        {
            return SyncResult.Failed("Jira integration requires an API token credential.");
        }

        var lastCursor = ParseDate(context.Cursor.Get(CursorKey));
        var jql = BuildJql(context, lastCursor);

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

    private static string BuildJql(SyncContext context, DateTimeOffset? cursor)
    {
        var clauses = new List<string>();

        if (TryGet(context, "jql", out var jql))
        {
            clauses.Add($"({jql})");
        }
        else if (TryGet(context, "projectKeys", out var projects))
        {
            var keys = projects.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

    private static bool TryGet(SyncContext context, string key, out string value)
    {
        if (context.Settings.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw;
            return true;
        }

        value = string.Empty;
        return false;
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
