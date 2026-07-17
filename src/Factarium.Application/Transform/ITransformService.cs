namespace Factarium.Application.Transform;

public sealed record TransformResult(
    int Repositories,
    int Commits,
    int PullRequests,
    int Reviews,
    int IdentitiesEnsured,
    int Issues = 0);

/// <summary>
/// Turns raw GitHub records (bronze) into canonical entities (silver) and ensures
/// a SourceIdentity exists for every actor seen. Idempotent: re-running upserts.
/// </summary>
public interface ITransformService
{
    Task<TransformResult> TransformAsync(CancellationToken cancellationToken);
}
