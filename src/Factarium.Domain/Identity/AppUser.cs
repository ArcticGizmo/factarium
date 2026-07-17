namespace Factarium.Domain.Identity;

/// <summary>
/// A user account that can sign in to Factarium. In local single-user mode a
/// single well-known account is seeded; the model is shaped so real multi-user
/// auth (OIDC/JWT) can be layered on later without changing the schema.
/// </summary>
public class AppUser
{
    public Guid Id { get; set; }

    public required string UserName { get; set; }

    public required string DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
