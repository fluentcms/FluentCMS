namespace FluentCMS.Core.DependencyInjection;

internal static class HostErrorCodes
{
    public const string InvalidOption = "FCMS1101";
    public const string GeneratedSigningKeyInProduction = "FCMS1102";
    public const string NotRegistered = "FCMS1103";
    public const string PipelineMissing = "FCMS1104";
    public const string PublicOriginUnsetInProduction = "FCMS1105";
    public const string UnencryptedKeyRingInProduction = "FCMS1106";
}
