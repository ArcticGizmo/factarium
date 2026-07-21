namespace Factarium.Application.Configuration;

/// <summary>
/// User-editable targets for the Overview dashboard, edited in-screen and stored under
/// <see cref="SettingsKey"/>. Nulls mean "no target set".
/// </summary>
public sealed record OverviewTargets
{
    public const string SettingsKey = "targets.overview";

    /// <summary>Lower bound: percentage of issues done (higher is better).</summary>
    public double? IssueCompletionPctMin { get; init; } = 80;
}
