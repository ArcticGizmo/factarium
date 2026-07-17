namespace Factarium.Application.Configuration;

/// <summary>
/// Configuration for the DORA-ish delivery metrics. Bound from the "Factarium:Dora"
/// configuration section.
/// </summary>
public sealed class DoraOptions
{
    public const string SectionName = "Factarium:Dora";

    /// <summary>
    /// Branch whose merged PRs stand in as "deployments" (the v1 deploy proxy).
    /// </summary>
    public string DeployBranch { get; set; } = "main";
}
