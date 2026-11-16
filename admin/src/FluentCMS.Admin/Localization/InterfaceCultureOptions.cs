using FluentCMS.Admin.Configuration;

namespace FluentCMS.Admin.Localization;

// The interface cultures offered and the default, from the cultures member of config.json (spec 2.5), limited to the cultures
// the admin ships, since config.json checks a culture name by its shape only.
internal sealed class InterfaceCultureOptions
{
    // The cultures with resources: English (AQ14).
    public static readonly IReadOnlyList<string> Shipped = ["en"];

    public IReadOnlyList<string> Offered { get; init; } = Shipped;

    public string Default { get; init; } = Shipped[0];

    // Without a configuration, or with only cultures the admin does not ship, the shipped cultures.
    public static InterfaceCultureOptions From(AdminCultures? cultures)
    {
        if (cultures is null)
        {
            return new InterfaceCultureOptions();
        }

        var offered = cultures.Offered is null
            ? Shipped
            : Shipped.Where(shipped => cultures.Offered.Contains(shipped, StringComparer.OrdinalIgnoreCase)).ToList();
        if (offered.Count == 0)
        {
            return new InterfaceCultureOptions();
        }

        var preferred = offered.FirstOrDefault(name => string.Equals(name, cultures.Default, StringComparison.OrdinalIgnoreCase));
        return new InterfaceCultureOptions { Offered = offered, Default = preferred ?? offered[0] };
    }
}
