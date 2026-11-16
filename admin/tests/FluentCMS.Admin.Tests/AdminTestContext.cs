using System.Globalization;
using Bunit;
using FluentCMS.Admin.Configuration;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FluentCMS.Admin.Tests;

// The admin's services as AddFluentCmsAdmin registers them, from a valid config.json unless a test starts with another, and
// under the left-to-right pseudo-culture unless a test chooses another or the neutral resources.
public abstract class AdminTestContext : BunitContext
{
    public const string ValidConfiguration = """{ "apiBaseUrl": "https://cms.example.com" }""";

    private CultureInfo culture = CultureInfo.GetCultureInfo(PseudoCulture.LeftToRight);
    private bool pseudo = true;
    private bool started;

    protected AdminTestContext()
    {
        Document = JSInterop.SetupModule(DocumentInterop.ModulePath);
        Document.SetupVoid("setLanguage", _ => true).SetVoidResult();
        Document.SetupVoid("setProperty", _ => true).SetVoidResult();

        // HeadOutlet reads the title the page had before the app started.
        JSInterop.Setup<string>("Blazor._internal.PageTitle.getAndRemoveExistingTitle").SetResult("");
    }

    protected BunitJSInterop Document { get; }

    protected string Text(string key, params object[] arguments) =>
        Services.GetRequiredService<IStringLocalizer<AdminResources>>()[key, arguments];

    // Call before Start.
    protected void UseInterfaceCulture(string name) => culture = CultureInfo.GetCultureInfo(name);

    // The resources as they ship, for a test that asserts a text.
    protected void UseNeutralResources() => pseudo = false;

    protected void Start(string json = ValidConfiguration) => Start(AdminConfigurationReader.Read(json));

    private protected void Start(AdminStartup startup)
    {
        if (started)
        {
            throw new InvalidOperationException("The admin's services are registered once.");
        }

        started = true;
        Services.AddAdminStartup(startup, new InterfaceCulture(culture), new FormatService(culture, TimeZoneInfo.Utc));
        if (pseudo)
        {
            var resources = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
            Services.AddSingleton<IStringLocalizerFactory>(new PseudoStringLocalizerFactory(resources, culture));
        }
    }
}
