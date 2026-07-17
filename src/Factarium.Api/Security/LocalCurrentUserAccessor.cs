using Factarium.Application.Security;

namespace Factarium.Api.Security;

/// <summary>
/// Local single-user implementation: always resolves to the seeded local account
/// with an administrative role. Replaced by an HTTP-context/claims-backed accessor
/// when real authentication is introduced (Phase 8).
/// </summary>
public sealed class LocalCurrentUserAccessor : ICurrentUserAccessor
{
    private static readonly CurrentUser LocalCurrentUser = new(
        LocalUser.Id,
        LocalUser.UserName,
        LocalUser.DisplayName,
        Roles: [FactariumRoles.Administrator]);

    public CurrentUser Current => LocalCurrentUser;
}
