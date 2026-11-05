namespace FluentCMS.Core.DependencyInjection;

// What the authorization server signs tokens with. It implements this; without one nothing is signed.
internal interface ISigningKeyStatus
{
    // True when no certificate is configured and the key is one the server generated and stores itself.
    bool IsGenerated { get; }
}

internal sealed class GeneratedSigningKeyCheck(SecurityOptions options, ISigningKeyStatus? signingKey = null) : IProductionStartupCheck
{
    public const string Key = $"{FluentCmsOptions.Section}:{SecurityOptions.Section}:{nameof(SecurityOptions.AllowGeneratedSigningKey)}";

    public StartupRefusal? Check() => signingKey?.IsGenerated == true && !options.AllowGeneratedSigningKey
        ? new StartupRefusal(
            HostErrorCodes.GeneratedSigningKeyInProduction,
            $"Production signs tokens with a generated key. Configure a signing certificate, or set {Key} to true to accept the generated key.")
        : null;
}
