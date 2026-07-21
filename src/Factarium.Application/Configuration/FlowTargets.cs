namespace Factarium.Application.Configuration;

/// <summary>
/// User-editable targets for the Flow dashboard, edited in-screen and stored under
/// <see cref="SettingsKey"/>. All are upper bounds (lower is better) on process-churn signals;
/// nulls mean "no target set". Reopens/backflow default to 0 (aspirational: none); the more
/// team-specific reassignment and blocked-time ceilings default to unset.
/// </summary>
public sealed record FlowTargets
{
    public const string SettingsKey = "targets.flow";

    public double? ReopensMax { get; init; } = 0;

    public double? BackflowMax { get; init; } = 0;

    public double? ReassignmentsMax { get; init; }

    public double? BlockedHoursMax { get; init; }
}
