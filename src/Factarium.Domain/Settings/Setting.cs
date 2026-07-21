namespace Factarium.Domain.Settings;

/// <summary>
/// Generic key→value application setting, stored as JSON text. Small pieces of
/// user-editable configuration (e.g. per-dashboard targets) live here rather than in
/// appsettings, so they can be changed in-screen and shared across everyone hitting the
/// same Postgres. The key namespaces the value (e.g. "targets.delivery").
/// </summary>
public class Setting
{
    public required string Key { get; set; }

    /// <summary>The value as JSON text; shape is owned by whoever reads the key.</summary>
    public required string Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
