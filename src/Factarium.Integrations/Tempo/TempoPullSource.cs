using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Factarium.Application.Sync;
using Microsoft.Extensions.Logging;

namespace Factarium.Integrations.Tempo;

/// <summary>
/// Pull source for Tempo Timesheets. A single <c>worklog</c> entity, scoped to one Jira project
/// and floored at <c>SyncSince</c>. Each raw worklog is flattened to the fields Factarium keeps
/// (identity, issue, author, seconds, work date) so bronze isn't a raw dump — mirroring the
/// GitHub/Jira flatten step. Worklogs upsert by their Tempo id, so a re-pull is idempotent.
/// </summary>
public sealed class TempoPullSource(TempoApiClient client, ILogger<TempoPullSource> logger) : IPullSource
{
    private const string Source = "tempo";
    private const string CursorKey = "worklogs:updated";
    private const int BatchSize = 200;

    public string Type => Source;

    public IReadOnlyList<string> Entities { get; } = ["worklog"];

    public IReadOnlyList<string> CursorKeyPrefixesForEntity(string entityType) =>
        entityType == "worklog" ? [CursorKey] : [];

    public async Task<SyncResult> PullEntityAsync(string entity, SyncContext context, CancellationToken cancellationToken)
    {
        if (entity != "worklog")
        {
            return SyncResult.Ok(0);
        }

        if (context.Config is not TempoSourceConfig config)
        {
            return SyncResult.Failed("Tempo integration is misconfigured (expected Tempo configuration).");
        }

        if (string.IsNullOrWhiteSpace(context.Credential))
        {
            return SyncResult.Failed("Tempo integration requires an API token credential.");
        }

        var from = config.SyncSince is { } since ? DateOnly.FromDateTime(since.UtcDateTime) : (DateOnly?)null;
        var batch = new List<RawFact>();
        var written = 0;
        DateTimeOffset? maxUpdated = null;

        await foreach (var worklog in client.GetWorklogsAsync(config.ProjectKey, from, context.Credential, cancellationToken))
        {
            var id = Id(worklog);
            if (id is null)
            {
                continue;
            }

            var updated = ParseDate(Str(worklog, "updatedAt"));
            batch.Add(new RawFact(Source, "worklog", id, FlattenWorklog(worklog), updated));
            if (updated is not null && (maxUpdated is null || updated > maxUpdated))
            {
                maxUpdated = updated;
            }

            if (batch.Count >= BatchSize)
            {
                written += await FlushAsync(context, batch, maxUpdated, cancellationToken);
            }
        }

        written += await FlushAsync(context, batch, maxUpdated, cancellationToken);
        logger.LogInformation("Tempo worklog sync wrote {Count} for project {Project}", written, config.ProjectKey);
        return SyncResult.Ok(written);
    }

    private static async Task<int> FlushAsync(
        SyncContext context, List<RawFact> batch, DateTimeOffset? mark, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return 0;
        }

        var written = await context.Sink.WriteAsync(context.IntegrationId, batch, cancellationToken);
        if (mark is not null)
        {
            context.Cursor.Set(CursorKey, mark.Value.ToUniversalTime().ToString("o"));
        }

        batch.Clear();
        return written;
    }

    /// <summary>
    /// Flattens a Tempo worklog into the fields Factarium keeps: identity (tempo/jira worklog
    /// ids), the issue it's booked against, the author account, logged/billable seconds, the
    /// work date, and the description. Tempo's work-attribute blobs and links are discarded.
    /// </summary>
    private static string FlattenWorklog(JsonElement worklog)
    {
        var issue = Obj(worklog, "issue");
        var author = Obj(worklog, "author");

        var node = new JsonObject
        {
            ["tempo_worklog_id"] = Num(worklog, "tempoWorklogId"),
            ["jira_worklog_id"] = Num(worklog, "jiraWorklogId"),
            ["issue_key"] = Str(issue, "key"),
            ["issue_id"] = Num(issue, "id"),
            ["author_id"] = Str(author, "accountId"),
            ["time_spent_seconds"] = Num(worklog, "timeSpentSeconds"),
            ["billable_seconds"] = Num(worklog, "billableSeconds"),
            ["work_date"] = Str(worklog, "startDate"),
            ["description"] = Str(worklog, "description"),
            ["created_at"] = Str(worklog, "createdAt"),
            ["updated_at"] = Str(worklog, "updatedAt"),
        };

        return node.ToJsonString();
    }

    private static string? Id(JsonElement worklog) =>
        worklog.TryGetProperty("tempoWorklogId", out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64().ToString(CultureInfo.InvariantCulture)
            : null;

    private static JsonElement Obj(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static JsonNode? Num(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? JsonValue.Create(value.GetInt64())
            : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
}
