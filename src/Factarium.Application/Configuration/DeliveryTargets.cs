namespace Factarium.Application.Configuration;

/// <summary>
/// User-editable targets for the Delivery dashboard, stored in the settings table under
/// <see cref="SettingsKey"/> and edited in-screen. Each is a threshold the dashboard
/// compares the live value against; nulls mean "no target set" (no chip/band shown).
/// Duration targets are upper bounds (lower is better); deploy frequency is a lower bound.
/// Defaults approximate DORA "high performer" bands so a fresh install shows useful
/// reference lines before anyone tunes them.
/// </summary>
public sealed record DeliveryTargets
{
    public const string SettingsKey = "targets.delivery";

    /// <summary>Lower bound: merges to the deploy branch per week (higher is better).</summary>
    public double? DeploysPerWeek { get; init; } = 5;

    /// <summary>Upper bound: average PR open→merge hours (lower is better).</summary>
    public double? LeadTimeHours { get; init; } = 24;

    /// <summary>Upper bound: average issue created→resolved hours (lower is better).</summary>
    public double? CycleTimeHours { get; init; } = 48;

    /// <summary>Upper bound: average hours to first PR review (lower is better).</summary>
    public double? ReviewLatencyHours { get; init; } = 24;
}
