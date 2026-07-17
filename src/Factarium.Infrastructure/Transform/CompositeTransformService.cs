using Factarium.Application.Transform;

namespace Factarium.Infrastructure.Transform;

/// <summary>Runs every source transform (GitHub, then Jira) as one step.</summary>
internal sealed class CompositeTransformService(
    GitHubTransformService github,
    JiraTransformService jira) : ITransformService
{
    public async Task<TransformResult> TransformAsync(CancellationToken cancellationToken)
    {
        var githubResult = await github.TransformAsync(cancellationToken);
        var issues = await jira.TransformAsync(cancellationToken);
        return githubResult with { Issues = issues };
    }
}
