using Factarium.Application.Sync;
using Factarium.Domain.Sync;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Factarium.Infrastructure.Sync;

internal sealed class PushIngestionService(
    FactariumDbContext db,
    IEnumerable<IPushSource> pushSources,
    IRawRecordSink sink,
    IRawRecordReader reader,
    TimeProvider clock,
    ILogger<PushIngestionService> logger) : IPushIngestionService
{
    private static readonly Dictionary<string, string> IntegrationNames = new()
    {
        ["claude-code"] = "Claude Code (OTEL)",
    };

    public async Task<SyncResult> IngestAsync(string sourceType, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var source = pushSources.FirstOrDefault(
            s => string.Equals(s.Type, sourceType, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            return SyncResult.Failed($"No push source registered for type '{sourceType}'.");
        }

        var integration = await EnsureIntegrationAsync(sourceType, cancellationToken);

        var context = new SyncContext
        {
            IntegrationId = integration.Id,
            IntegrationName = integration.Name,
            Credential = null,
            Config = null,
            Cursor = new DictionaryCursorStore(),
            Sink = sink,
            Reader = reader,
        };

        var now = clock.GetUtcNow();
        SyncResult result;
        try
        {
            result = await source.IngestAsync(context, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Push ingestion failed for {Type}", sourceType);
            result = SyncResult.Failed(ex.Message);
        }

        integration.LastRunStartedAt ??= now;
        integration.LastRunCompletedAt = clock.GetUtcNow();
        integration.LastRunRecordsWritten = result.RecordsWritten;
        integration.LastRunStatus = result.Succeeded ? SyncRunStatus.Success : SyncRunStatus.Failed;
        integration.LastRunError = result.Error;
        await db.SaveChangesAsync(cancellationToken);

        return result;
    }

    private async Task<Integration> EnsureIntegrationAsync(string sourceType, CancellationToken cancellationToken)
    {
        var integration = await db.Integrations
            .FirstOrDefaultAsync(i => i.Type == sourceType, cancellationToken);

        if (integration is not null)
        {
            return integration;
        }

        integration = CreatePushIntegration(sourceType);
        db.Integrations.Add(integration);
        await db.SaveChangesAsync(cancellationToken);
        return integration;
    }

    private Integration CreatePushIntegration(string sourceType)
    {
        var name = IntegrationNames.GetValueOrDefault(sourceType, $"{sourceType} (push)");

        Integration integration = sourceType switch
        {
            ClaudeIntegration.TypeName => new ClaudeIntegration { Name = name },
            _ => throw new InvalidOperationException(
                $"No integration entity mapping for push source '{sourceType}'."),
        };

        integration.Id = Guid.NewGuid();
        integration.Enabled = true;
        integration.ScheduleCron = null;
        integration.CreatedAt = clock.GetUtcNow();
        return integration;
    }
}
