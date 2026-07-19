using Factarium.Domain.Sync;

namespace Factarium.Application.Sync;

/// <summary>
/// Runs a single integration's sync: resolves the source, decrypts its credential,
/// loads/persists cursor state, records run status and history, and writes facts to
/// the bronze tier. Callers (the scheduler, the "run now" endpoint) invoke this per
/// integration, passing how the run was triggered.
/// </summary>
public interface IIntegrationSyncService
{
    Task<SyncResult> RunAsync(Guid integrationId, SyncRunTrigger trigger, CancellationToken cancellationToken);
}
