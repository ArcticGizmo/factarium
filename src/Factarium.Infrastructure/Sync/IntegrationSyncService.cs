using System.Text.Json;
using Factarium.Application.Security;
using Factarium.Application.Sync;
using Factarium.Domain.Sync;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Factarium.Infrastructure.Sync;

internal sealed class IntegrationSyncService(
    FactariumDbContext db,
    IEnumerable<IPullSource> pullSources,
    IRawRecordSink sink,
    ICredentialProtector protector,
    TimeProvider clock,
    ILogger<IntegrationSyncService> logger) : IIntegrationSyncService
{
    public async Task<SyncResult> RunAsync(Guid integrationId, SyncRunTrigger trigger, CancellationToken cancellationToken)
    {
        var integration = await db.Integrations
            .FirstOrDefaultAsync(i => i.Id == integrationId, cancellationToken)
            ?? throw new InvalidOperationException($"Integration {integrationId} not found.");

        // Open a history row and mark the connection running up front, so the run is
        // visible while it executes and even the misconfigured paths below are recorded.
        var startedAt = clock.GetUtcNow();
        var run = new SyncRun
        {
            IntegrationId = integration.Id,
            IntegrationName = integration.Name,
            IntegrationType = integration.Type,
            Trigger = trigger,
            Status = SyncRunStatus.Running,
            StartedAt = startedAt,
        };
        db.SyncRuns.Add(run);

        integration.LastRunStartedAt = startedAt;
        integration.LastRunStatus = SyncRunStatus.Running;
        integration.LastRunError = null;
        await db.SaveChangesAsync(cancellationToken);

        var source = pullSources.FirstOrDefault(
            s => string.Equals(s.Type, integration.Type, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            return await FinishAsync(
                integration,
                run,
                SyncResult.Failed($"No pull source registered for type '{integration.Type}'."),
                cursor: null,
                cancellationToken);
        }

        var cursor = new DictionaryCursorStore(ParseDictionary(integration.CursorState));
        SyncResult result;
        try
        {
            var credential = integration.EncryptedCredential is null
                ? null
                : protector.Unprotect(integration.EncryptedCredential);

            var context = new SyncContext
            {
                IntegrationId = integration.Id,
                IntegrationName = integration.Name,
                Credential = credential,
                Config = BuildConfig(integration),
                Cursor = cursor,
                Sink = sink,
            };

            logger.LogInformation("Starting sync for integration {Name} ({Type})", integration.Name, integration.Type);
            result = await source.PullAsync(context, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sync failed for integration {Name}", integration.Name);
            result = SyncResult.Failed(ex.Message);
        }

        return await FinishAsync(integration, run, result, cursor, cancellationToken);
    }

    private async Task<SyncResult> FinishAsync(
        Integration integration,
        SyncRun run,
        SyncResult result,
        DictionaryCursorStore? cursor,
        CancellationToken cancellationToken)
    {
        if (cursor is { Dirty: true })
        {
            integration.CursorState = JsonSerializer.Serialize(cursor.Snapshot());
        }

        var completedAt = clock.GetUtcNow();
        var status = result.Succeeded ? SyncRunStatus.Success : SyncRunStatus.Failed;

        // Stamp the history row and mirror the outcome onto the connection's latest-run fields.
        run.CompletedAt = completedAt;
        run.RecordsWritten = result.RecordsWritten;
        run.Status = status;
        run.Error = result.Error;

        integration.LastRunCompletedAt = completedAt;
        integration.LastRunRecordsWritten = result.RecordsWritten;
        integration.LastRunStatus = status;
        integration.LastRunError = result.Error;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Sync for {Name} finished: {Status}, {Records} records",
            integration.Name, integration.LastRunStatus, result.RecordsWritten);

        return result;
    }

    private static SourceConfig? BuildConfig(Integration integration) => integration switch
    {
        GitHubIntegration gh => new GitHubSourceConfig(gh.Org, gh.Repos),
        JiraIntegration jira => new JiraSourceConfig(jira.BaseUrl, jira.Email, jira.ProjectKey, jira.SyncSince, jira.ScopedToken),
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
