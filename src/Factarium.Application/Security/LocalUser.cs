namespace Factarium.Application.Security;

/// <summary>
/// Well-known values for the single account used in local single-user mode.
/// The same identifier is seeded into the database and returned by the local
/// <see cref="ICurrentUserAccessor"/>, so attribution stays consistent if/when
/// real auth is introduced.
/// </summary>
public static class LocalUser
{
    public static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public const string UserName = "local";

    public const string DisplayName = "Local User";
}
