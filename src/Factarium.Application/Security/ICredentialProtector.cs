namespace Factarium.Application.Security;

/// <summary>
/// Encrypts/decrypts integration credentials at rest. Backed by ASP.NET Data
/// Protection so no external key vault is required for local use, while keeping
/// tokens out of plaintext in the database.
/// </summary>
public interface ICredentialProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}
