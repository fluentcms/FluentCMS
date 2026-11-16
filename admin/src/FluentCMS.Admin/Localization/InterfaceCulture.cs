using System.Globalization;

namespace FluentCMS.Admin.Localization;

// The culture of the interface, chosen once at start: Blazor WebAssembly loads the resources of the current culture when the
// host starts, so a change of culture reloads the app.
internal sealed class InterfaceCulture(CultureInfo culture)
{
    public CultureInfo Culture { get; } = culture;

    public string Direction => TextDirection.Of(Culture);

    public static CultureInfo Choose(string? preferred, CultureInfo browser, InterfaceCultureOptions options)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(options);

        return Offered(TryGet(preferred), options)
            ?? Offered(browser, options)
            ?? CultureInfo.GetCultureInfo(options.Default);
    }

    // The interface culture sets the resources and the direction; the format culture, the user's own, sets dates and numbers.
    public void Apply(CultureInfo formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = formats;
        CultureInfo.CurrentCulture = formats;
    }

    // An offered culture matches the culture itself or one of its parents, so a browser in en-GB gets en.
    private static CultureInfo? Offered(CultureInfo? culture, InterfaceCultureOptions options)
    {
        for (var current = culture; current is not null && current.Name.Length > 0; current = current.Parent)
        {
            var match = options.Offered.FirstOrDefault(name => string.Equals(name, current.Name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return CultureInfo.GetCultureInfo(match);
            }
        }

        return null;
    }

    private static CultureInfo? TryGet(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
