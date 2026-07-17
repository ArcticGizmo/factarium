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
    public async Task<SyncResult> RunAsync(Guid integrationId, CancellationToken cancellationToken)
    {
        var integration = await db.Integrations
            .FirstOrDefaultAsync(i => i.Id == integrationId, cancellationToken)
            ?? throw new InvalidOperationException($"Integration {integrationId} not found.");

        var source = pullSources.FirstOrDefault(
            s => string.Equals(s.Type, integration.Type, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            return await FinishAsync(
                integration,
                SyncResult.Failed($"No pull source registered for type '{integration.Type}'."),
                cursor: null,
                cancellationToken);
        }

        integration.LastRunStartedAt = clock.GetUtcNow();
        integration.LastRunStatus = SyncRunStatus.Running;
        integration.LastRunError = null;
        await db.SaveChangesAsync(cancellationToken);

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
                Settings = ParseDictionary(integration.SettingsJson),
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

        return await FinishAsync(integration, result, cursor, cancellationToken);
    }

    private async Task<SyncResult> FinishAsync(
        Integration integration,
        SyncResult result,
        DictionaryCursorStore? cursor,
        CancellationToken cancellationToken)
    {
        if (cursor is { Dirty: true })
        {
            integration.CursorState = JsonSerializer.Serialize(cursor.Snapshot());
        }

        integration.LastRunCompletedAt = clock.GetUtcNow();
        integration.LastRunRecordsWritten = result.RecordsWritten;
        integration.LastRunStatus = result.Succeeded ? SyncRunStatus.Success : SyncRunStatus.Failed;
        integration.LastRunError = result.Error;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Sync for {Name} finished: {Status}, {Records} records",
            integration.Name, integration.LastRunStatus, result.RecordsWritten);

        return result;
    }

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
