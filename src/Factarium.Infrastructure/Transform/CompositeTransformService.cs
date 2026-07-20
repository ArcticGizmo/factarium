using Factarium.Application.Transform;

namespace Factarium.Infrastructure.Transform;

/// <summary>Runs every source transform (GitHub, then Jira + its changelog flow) as one step.</summary>
internal sealed class CompositeTransformService(
    GitHubTransformService github,
    JiraTransformService jira,
    JiraChangelogTransformService jiraChangelog,
    TempoTransformService tempo,
    ClaudeOtelTransformService claudeOtel) : ITransformService
{
    public async Task<TransformResult> TransformAsync(CancellationToken cancellationToken)
    {
        var githubResult = await github.TransformAsync(cancellationToken);
        var issues = await jira.TransformAsync(cancellationToken);

        // Runs after the issue transform: it reads the canonical issues and updates their churn.
        await jiraChangelog.TransformAsync(cancellationToken);

        var worklogs = await tempo.TransformAsync(cancellationToken);
        var usageMetrics = await claudeOtel.TransformAsync(cancellationToken);
        return githubResult with { Issues = issues, UsageMetrics = usageMetrics, Worklogs = worklogs };
    }
}
