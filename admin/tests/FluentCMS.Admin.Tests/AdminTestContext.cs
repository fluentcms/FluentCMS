using System.Globalization;
using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Authentication;
using FluentCMS.Admin.Configuration;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Tests.Authentication;
using FluentCMS.Admin.Tests.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FluentCMS.Admin.Tests;

// The admin's services as AddFluentCmsAdmin registers them, from a valid config.json unless a test starts with another, and
// under the left-to-right pseudo-culture unless a test chooses another or the neutral resources. The tab is signed in unless
// a test runs the sign-in itself, against a fake authorization server and an in-memory sessionStorage, and the shell reads
// a fake Management API.
public abstract class AdminTestContext : BunitContext
{
    public const string ValidConfiguration = """{ "apiBaseUrl": "https://cms.example.com" }""";

    private CultureInfo culture = CultureInfo.GetCultureInfo(PseudoCulture.LeftToRight);
    private bool pseudo = true;
    private bool signInFlow;
    private bool started;

    protected AdminTestContext()
    {
        // The library under the wrapped set calls its scripts, which do not run under bUnit; the admin's own calls are set up.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Document = JSInterop.SetupModule(DocumentInterop.ModulePath);
        Document.SetupVoid("setLanguage", _ => true).SetVoidResult();
        Document.SetupVoid("setProperty", _ => true).SetVoidResult();
        Document.SetupVoid("focusHeading", _ => true).SetVoidResult();

        // HeadOutlet reads the title the page had before the app started.
        JSInterop.Setup<string>("Blazor._internal.PageTitle.getAndRemoveExistingTitle").SetResult("");
    }

    protected BunitJSInterop Document { get; }

    // Shared by the page loads of one tab, which a test makes as contexts of their own.
    internal MemorySessionStorage Storage { get; set; } = new();

    internal FakeAuthority Authority { get; set; } = new();

    internal FakeManagementServer Management { get; set; } = new();

    protected string Text(string key, params object[] arguments) =>
        Services.GetRequiredService<IStringLocalizer<AdminResources>>()[key, arguments];

    // Call before Start.
    protected void UseInterfaceCulture(string name) => culture = CultureInfo.GetCultureInfo(name);

    // The resources as they ship, for a test that asserts a text.
    protected void UseNeutralResources() => pseudo = false;

    // Call before Start: the real session, which signs in against Authority.
    protected void UseSignInFlow() => signInFlow = true;

    protected void Start(string json = ValidConfiguration) => Start(AdminConfigurationReader.Read(json));

    private protected void Start(AdminStartup startup)
    {
        if (started)
        {
            throw new InvalidOperationException("The admin's services are registered once.");
        }

        started = true;
        Services.AddAdminStartup(startup, new InterfaceCulture(culture), new FormatService(culture, TimeZoneInfo.Utc));
        if (startup.Configuration is not null)
        {
            Services.Replace(ServiceDescriptor.Scoped<ISessionStorage>(_ => Storage));
            Services.Configure<AuthorityOptions>(options => options.PrimaryHandler = () => Authority);
            Services.Configure<ManagementApiOptions>(options => options.PrimaryHandler = () => Management);
            if (!signInFlow)
            {
                Services.Replace(ServiceDescriptor.Scoped<IAdminSession, SignedInTestSession>());
            }
        }

        if (pseudo)
        {
            var resources = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
            Services.AddSingleton<IStringLocalizerFactory>(new PseudoStringLocalizerFactory(resources, culture));
        }
    }
}
