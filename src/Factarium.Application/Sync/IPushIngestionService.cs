namespace Factarium.Application.Sync;

/// <summary>
/// Entry point for push/event ingestion (OTLP receivers, webhooks). Resolves the
/// push source by type, ensures a backing integration row exists, and writes the
/// payload's facts to the bronze tier — the push counterpart to
/// <see cref="IIntegrationSyncService"/>.
/// </summary>
public interface IPushIngestionService
{
    Task<SyncResult> IngestAsync(string sourceType, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}
