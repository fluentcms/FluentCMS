using FluentCMS.Core.Identity.Oidc;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Core.DependencyInjection;

// Not in Development, so that its report keeps data-protection-keys-unencrypted.
internal sealed class UnencryptedKeyRingCheck(SecurityOptions options, IDataProtectionStatus? keyRing = null, IHostEnvironment? environment = null) : IProductionStartupCheck
{
    public const string Key = $"{FluentCmsOptions.Section}:{SecurityOptions.Section}:{nameof(SecurityOptions.AllowUnencryptedKeyRing)}";

    public StartupRefusal? Check() => environment?.IsDevelopment() == false && keyRing?.KeysEncrypted == false && !options.AllowUnencryptedKeyRing
        ? new StartupRefusal(
            HostErrorCodes.UnencryptedKeyRingInProduction,
            $"The host stores the Data Protection key ring unencrypted, so a reader of the database can forge sign-in cookies. Add an XML encryptor, such as AddDataProtection().ProtectKeysWithCertificate(...) before AddFluentCms, or set {Key} to true to accept the unencrypted ring.")
        : null;
}
