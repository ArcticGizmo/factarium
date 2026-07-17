using Factarium.Application.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Factarium.Infrastructure.Security;

internal sealed class DataProtectionCredentialProtector : ICredentialProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionCredentialProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("Factarium.IntegrationCredential.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
