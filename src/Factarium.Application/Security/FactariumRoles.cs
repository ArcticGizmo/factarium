namespace Factarium.Application.Security;

/// <summary>
/// Role names used by authorization policies. Declared from day 0 so
/// <c>[Authorize]</c>-style policy checks can be attached to endpoints now
/// (permissive in local mode) and enforced once real auth arrives.
/// </summary>
public static class FactariumRoles
{
    public const string Administrator = "administrator";
    public const string Viewer = "viewer";
}
