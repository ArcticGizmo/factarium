namespace Factarium.Application.Configuration;

/// <summary>
/// User-editable targets for the Repositories dashboard, edited in-screen and stored under
/// <see cref="SettingsKey"/>. Nulls mean "no target set".
/// </summary>
public sealed record RepoActivityTargets
{
    public const string SettingsKey = "targets.repositories";

    /// <summary>Upper bound: identities still unmapped to a Person (a data-quality smell).</summary>
    public double? UnmappedIdentitiesMax { get; init; } = 0;
}
