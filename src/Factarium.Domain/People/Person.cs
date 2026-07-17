namespace Factarium.Domain.People;

/// <summary>
/// A first-class person. Integration-specific identities (GitHub logins, Jira
/// accounts, …) are linked to a Person so all their activity is attributed
/// together, even when one human appears under several logins.
/// </summary>
public class Person
{
    public Guid Id { get; set; }

    public required string DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
