using System.Globalization;
using FluentCMS.Admin.Localization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace FluentCMS.Admin;

public static class AdminHostBuilderExtensions
{
    // Asynchronous because it will read config.json before anything else registers (spec 2.5).
    public static Task AddFluentCmsAdmin(this WebAssemblyHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // At start the runtime has set the current culture to the browser's. The culture must be set before the host runs,
        // which loads its resources.
        var browser = CultureInfo.CurrentCulture;
        var culture = new InterfaceCulture(
            InterfaceCulture.Choose(new NoInterfaceCulturePreference().Culture, browser, new InterfaceCultureOptions()));
        culture.Apply(browser);

        builder.Services
            .AddAdminLocalization(culture, new FormatService(browser, TimeZoneInfo.Local))
            .AddAdminAccessibility();
        return Task.CompletedTask;
    }
}
