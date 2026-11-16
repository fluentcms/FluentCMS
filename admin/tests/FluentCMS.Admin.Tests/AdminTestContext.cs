using System.Globalization;
using Bunit;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FluentCMS.Admin.Tests;

// The admin's services as the app registers them, under the left-to-right pseudo-culture unless a test chooses another.
public abstract class AdminTestContext : BunitContext
{
    protected AdminTestContext()
    {
        Document = JSInterop.SetupModule(DocumentInterop.ModulePath);
        Document.SetupVoid("setLanguage", _ => true).SetVoidResult();
        Services.AddAdminAccessibility();
        UseInterfaceCulture(PseudoCulture.LeftToRight);
    }

    protected BunitJSInterop Document { get; }

    // Call before the first render, which builds the services.
    protected void UseInterfaceCulture(string name)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        Services.AddAdminLocalization(new InterfaceCulture(culture), new FormatService(culture, TimeZoneInfo.Utc));
        var resources = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        Services.AddSingleton<IStringLocalizerFactory>(new PseudoStringLocalizerFactory(resources, culture));
    }
}
