namespace Factarium.Application.Sync;

/// <summary>
/// Strongly-typed, per-source configuration handed to a connector for one sync run.
/// Each concrete source verifies and consumes its own shape rather than reading a
/// loose settings dictionary by string key.
/// </summary>
public abstract record SourceConfig;

/// <summary>GitHub connector config: an org and/or an explicit repo list.</summary>
public sealed record GitHubSourceConfig(string? Org, IReadOnlyList<string> Repos) : SourceConfig;

/// <summary>Jira connector config: site URL, account email, a single project, a history floor, and token mode.</summary>
public sealed record JiraSourceConfig(
    string? BaseUrl,
    string? Email,
    string? ProjectKey,
    DateTimeOffset? SyncSince,
    bool ScopedToken) : SourceConfig;

/// <summary>Tempo connector config: the Jira project to scope worklogs to, and a history floor.</summary>
public sealed record TempoSourceConfig(string? ProjectKey, DateTimeOffset? SyncSince) : SourceConfig;

/// <summary>Claude Code connector config. Push-based, so it carries nothing today.</summary>
public sealed record ClaudeSourceConfig : SourceConfig;
