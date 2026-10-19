namespace FluentCMS.Host.Security;

// Where Data Protection keeps the keys that protect cookies, anti-forgery tokens and stored secrets such as webhook secrets (SEC-07).
public sealed class KeyRingOptions
{
    public const string SectionName = "DataProtection";

    // Relative to the content root. It has to survive restarts and upgrades, and only the host's account should read it.
    public string KeysDirectory { get; set; } = "data-protection-keys";
}
