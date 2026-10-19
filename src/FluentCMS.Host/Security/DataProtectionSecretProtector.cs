using FluentCMS.Core.Security;
using Microsoft.AspNetCore.DataProtection;

namespace FluentCMS.Host.Security;

// Stored secrets use the key ring that protects cookies, so the web and stdio hosts read each other's, and losing the key ring
// loses them with the sign-ins (SEC-07).
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    public string Protect(string purpose, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(secret);

        return provider.CreateProtector(purpose).Protect(secret);
    }

    public string Unprotect(string purpose, string protectedSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(protectedSecret);

        return provider.CreateProtector(purpose).Unprotect(protectedSecret);
    }
}
