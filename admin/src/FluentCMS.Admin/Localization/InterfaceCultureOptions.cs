namespace FluentCMS.Admin.Localization;

// The interface cultures offered and the default, the cultures member of config.json (spec 2.5). Until the configuration is
// read, the shipped cultures: English (AQ14).
internal sealed class InterfaceCultureOptions
{
    public IReadOnlyList<string> Offered { get; init; } = ["en"];

    public string Default { get; init; } = "en";
}
