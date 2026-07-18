namespace Factarium.Application.Sync;

/// <summary>
/// Strongly-typed, per-source configuration handed to a connector for one sync run.
/// Each concrete source verifies and consumes its own shape rather than reading a
/// loose settings dictionary by string key.
/// </summary>
public abstract record SourceConfig;

/// <summary>GitHub connector config: an org and/or an explicit repo list.</summary>
public sealed record GitHubSourceConfig(string? Org, IReadOnlyList<string> Repos) : SourceConfig;

/// <summary>Jira connector config: site URL, account email, and project scope.</summary>
public sealed record JiraSourceConfig(
    string? BaseUrl,
    string? Email,
    IReadOnlyList<string> ProjectKeys,
    string? Jql) : SourceConfig;

/// <summary>Claude Code connector config. Push-based, so it carries nothing today.</summary>
public sealed record ClaudeSourceConfig : SourceConfig;
