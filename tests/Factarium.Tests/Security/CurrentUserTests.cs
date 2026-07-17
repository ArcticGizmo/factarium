using Factarium.Application.Security;

namespace Factarium.Tests.Security;

public class CurrentUserTests
{
    [Fact]
    public void IsInRole_is_case_insensitive()
    {
        var user = new CurrentUser(
            LocalUser.Id,
            LocalUser.UserName,
            LocalUser.DisplayName,
            Roles: [FactariumRoles.Administrator]);

        Assert.True(user.IsInRole("administrator"));
        Assert.True(user.IsInRole("ADMINISTRATOR"));
        Assert.False(user.IsInRole(FactariumRoles.Viewer));
    }

    [Fact]
    public void LocalUser_id_is_stable()
    {
        // The seeded local user id must never drift, or attribution breaks
        // when real auth is introduced later.
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"), LocalUser.Id);
    }
}
