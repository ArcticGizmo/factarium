using System.Text.Json;
using Factarium.Application.Security;
using Factarium.Application.Sync;
using Factarium.Domain.Sync;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Factarium.Infrastructure.Sync;

/// <summary>
/// Runs an integration sync as a sequence of independent per-entity units. Each entity
/// gets its own <see cref="SyncRun"/> history row, commits and advances its own cursor
/// on its own, and a failure in one entity never rolls back another's records. The
/// connection's own last-run fields hold an aggregate across the entities.
/// </summary>
internal sealed class IntegrationSyncService(
    FactariumDbContext db,
    IEnumerable<IPullSource> pullSources,
    IRawRecordSink sink,
    IRawRecordReader reader,
    ICredentialProtector protector,
    TimeProvider clock,
    ILogger<IntegrationSyncService> logger) : IIntegrationSyncService
{
    public async Task<SyncResult> RunAsync(
        Guid integrationId, SyncRunTrigger trigger, string? entity, CancellationToken cancellationToken)
    {
        var integration = await db.Integrations
            .FirstOrDefaultAsync(i => i.Id == integrationId, cancellationToken)
            ?? throw new InvalidOperationException($"Integration {integrationId} not found.");

        var source = pullSources.FirstOrDefault(
            s => string.Equals(s.Type, integration.Type, StringComparison.OrdinalIgnoreCase));

        var startedAt = clock.GetUtcNow();
        integration.LastRunStartedAt = startedAt;
        integration.LastRunStatus = SyncRunStatus.Running;
        integration.LastRunError = null;
        integration.LastRunCompletedAt = null;
        await db.SaveChangesAsync(cancellationToken);

        if (source is null)
        {
            // No connector for this type (e.g. a push-based integration triggered by hand):
            // record a single failed run with no entity, mirror it onto the connection.
            return await RecordSingleFailureAsync(
                integration, entity, trigger, startedAt,
                $"No pull source registered for type '{integration.Type}'.", cancellationToken);
        }

        // A specific entity was requested: run only that one (rejecting an unknown name).
        IReadOnlyList<string> entities = source.Entities;
        if (entity is not null)
        {
            var match = source.Entities.FirstOrDefault(e => string.Equals(e, entity, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return await RecordSingleFailureAsync(
                    integration, entity, trigger, startedAt,
                    $"Unknown entity '{entity}' for integration type '{integration.Type}'.", cancellationToken);
            }

            entities = [match];
        }

        var cursor = new DictionaryCursorStore(ParseDictionary(integration.CursorState));
        var credential = integration.EncryptedCredential is null
            ? null
            : protector.Unprotect(integration.EncryptedCredential);

        // Persists cursor progress mid-entity (after a source flushes a batch), so an
        // interrupted run resumes from the last checkpoint rather than re-fetching the entity.
        async Task CheckpointCursorAsync(CancellationToken ct)
        {
            if (!cursor.Dirty)
            {
                return;
            }

            integration.CursorState = JsonSerializer.Serialize(cursor.Snapshot());
            await db.SaveChangesAsync(ct);
            cursor.ClearDirty();
        }

        var context = new SyncContext
        {
            IntegrationId = integration.Id,
            IntegrationName = integration.Name,
            Credential = credential,
            Config = BuildConfig(integration),
            Cursor = cursor,
            Sink = sink,
            Reader = reader,
            Checkpoint = CheckpointCursorAsync,
        };

        logger.LogInformation(
            "Starting sync for {Name} ({Type}): {Count} entities",
            integration.Name, integration.Type, entities.Count);

        var totalRecords = 0;
        var anyFailed = false;
        string? firstError = null;
        var lastCompleted = startedAt;

        foreach (var entityName in entities)
        {
            var run = NewRun(integration, entityName, trigger, clock.GetUtcNow());
            db.SyncRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken); // visible as Running while it executes

            SyncResult result;
            try
            {
                result = await source.PullEntityAsync(entityName, context, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sync entity {Entity} failed for {Name}", entityName, integration.Name);
                result = SyncResult.Failed(ex.Message);
            }

            lastCompleted = clock.GetUtcNow();
            Stamp(run, result, lastCompleted);

            // Persist this entity's cursor progress now, so a later entity's failure
            // can't undo it.
            if (cursor.Dirty)
            {
                integration.CursorState = JsonSerializer.Serialize(cursor.Snapshot());
            }

            await db.SaveChangesAsync(cancellationToken);

            totalRecords += result.RecordsWritten;
            if (!result.Succeeded)
            {
                anyFailed = true;
                firstError ??= result.Error;
            }
        }

        SetAggregate(integration, anyFailed, totalRecords, firstError, startedAt, lastCompleted);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Sync for {Name} finished: {Status}, {Records} records across {Count} entities",
            integration.Name, integration.LastRunStatus, totalRecords, entities.Count);

        return anyFailed ? SyncResult.Failed(firstError ?? "One or more entities failed.", totalRecords) : SyncResult.Ok(totalRecords);
    }

    // Records a single failed run (with the given entity label) and mirrors it onto the
    // connection's aggregate — used for "no pull source" and "unknown entity".
    private async Task<SyncResult> RecordSingleFailureAsync(
        Integration integration,
        string? entity,
        SyncRunTrigger trigger,
        DateTimeOffset startedAt,
        string error,
        CancellationToken cancellationToken)
    {
        var run = NewRun(integration, entity, trigger, startedAt);
        db.SyncRuns.Add(run);
        Stamp(run, SyncResult.Failed(error), clock.GetUtcNow());
        SetAggregate(integration, anyFailed: true, records: 0, firstError: error, startedAt, run.CompletedAt!.Value);
        await db.SaveChangesAsync(cancellationToken);
        return SyncResult.Failed(error);
    }

    private SyncRun NewRun(Integration integration, string? entity, SyncRunTrigger trigger, DateTimeOffset startedAt) =>
        new()
        {
            IntegrationId = integration.Id,
            IntegrationName = integration.Name,
            IntegrationType = integration.Type,
            EntityType = entity,
            Trigger = trigger,
            Status = SyncRunStatus.Running,
            StartedAt = startedAt,
        };

    private static void Stamp(SyncRun run, SyncResult result, DateTimeOffset completedAt)
    {
        run.CompletedAt = completedAt;
        run.RecordsWritten = result.RecordsWritten;
        run.Status = result.Succeeded ? SyncRunStatus.Success : SyncRunStatus.Failed;
        run.Error = result.Error;
    }

    private static void SetAggregate(
        Integration integration,
        bool anyFailed,
        int records,
        string? firstError,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        integration.LastRunStartedAt = startedAt;
        integration.LastRunCompletedAt = completedAt;
        integration.LastRunStatus = anyFailed ? SyncRunStatus.Failed : SyncRunStatus.Success;
        integration.LastRunRecordsWritten = records;
        integration.LastRunError = firstError;
    }

    private static SourceConfig? BuildConfig(Integration integration) => integration switch
    {
        GitHubIntegration gh => new GitHubSourceConfig(gh.Org, gh.Repos),
        JiraIntegration jira => new JiraSourceConfig(jira.BaseUrl, jira.Email, jira.ProjectKey, jira.SyncSince, jira.ScopedToken),
        TempoIntegration tempo => new TempoSourceConfig(tempo.ProjectKey, tempo.SyncSince),
        ClaudeIntegration => new ClaudeSourceConfig(),
        _ => null,
    };

    private static Dictionary<string, string?> ParseDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, string?>>(json)
               ?? new Dictionary<string, string?>();
    }
}
