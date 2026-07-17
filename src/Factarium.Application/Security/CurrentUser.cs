namespace Factarium.Application.Security;

/// <summary>
/// The identity of the caller for the current request/operation. Every use-case
/// resolves the caller through <see cref="ICurrentUserAccessor"/> rather than
/// touching HTTP context or claims directly, so the source of identity can change
/// (local seeded user today, OIDC/JWT later) without touching the application layer.
/// </summary>
public sealed record CurrentUser(
    Guid Id,
    string UserName,
    string DisplayName,
    IReadOnlyCollection<string> Roles)
{
    public bool IsInRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}

public interface ICurrentUserAccessor
{
    CurrentUser Current { get; }
}
