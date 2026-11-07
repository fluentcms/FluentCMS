using FluentCMS.Core.Identity.Oidc;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Core.DependencyInjection;

// Production only, so that the report of other environments keeps data-protection-keys-unencrypted.
internal sealed class UnencryptedKeyRingCheck(SecurityOptions options, IDataProtectionStatus? keyRing = null, IHostEnvironment? environment = null) : IProductionStartupCheck
{
    public const string Key = $"{FluentCmsOptions.Section}:{SecurityOptions.Section}:{nameof(SecurityOptions.AllowUnencryptedKeyRing)}";

    public StartupRefusal? Check() => environment?.IsProduction() == true && keyRing?.KeysEncrypted == false && !options.AllowUnencryptedKeyRing
        ? new StartupRefusal(
            HostErrorCodes.UnencryptedKeyRingInProduction,
            $"Production stores the Data Protection key ring unencrypted, so a reader of the database can forge sign-in cookies. Add an XML encryptor, such as AddDataProtection().ProtectKeysWithCertificate(...) after AddFluentCms, or set {Key} to true to accept the unencrypted ring.")
        : null;
}
