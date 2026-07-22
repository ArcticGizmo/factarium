namespace Factarium.Application.Configuration;

/// <summary>
/// User-editable targets for the Repositories dashboard, edited in-screen and stored under
/// <see cref="SettingsKey"/>. Nulls mean "no target set".
/// </summary>
public sealed record RepoActivityTargets
{
    public const string SettingsKey = "targets.repositories";

    /// <summary>Lower bound: share of PRs merged in the window that got a review before merge (%).</summary>
    public double? ReviewCoveragePctMin { get; init; } = 80;

    /// <summary>Upper bound: median size (lines changed) of PRs merged in the window.</summary>
    public double? MedianPrLinesMax { get; init; } = 400;

    /// <summary>Upper bound: age in days of the oldest still-open PR.</summary>
    public double? OldestOpenPrDaysMax { get; init; } = 14;

    /// <summary>Upper bound: share of PRs closed in the window that were abandoned without merging (%).</summary>
    public double? AbandonRatePctMax { get; init; } = 20;

    /// <summary>Upper bound: identities still unmapped to a Person (a data-quality smell).</summary>
    public double? UnmappedIdentitiesMax { get; init; } = 0;
}
