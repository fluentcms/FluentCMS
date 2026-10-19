using FluentCMS.Admin;
using FluentCMS.Admin.Components;
using FluentCMS.Api;
using FluentCMS.Core;
using FluentCMS.Core.Identity;
using FluentCMS.Host.Audit;
using FluentCMS.Host.Identity;
using FluentCMS.Host.Jobs;
using FluentCMS.Host.Media;
using FluentCMS.Host.Security;
using FluentCMS.Infrastructure;
using FluentCMS.Infrastructure.Media;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.Mcp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Console;

namespace FluentCMS.Host;

public static class FluentCmsHost
{
    public static async Task<int> RunWebAsync(string[] args)
    {
        var app = BuildWebApplication(args);
        await app.RunAsync();
        return 0;
    }

    public static async Task<int> RunMcpAsync(string[] args)
    {
        using var host = BuildMcpHost(args);
        await host.RunAsync();
        return 0;
    }

    public static WebApplication BuildWebApplication(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // First, so the settings a caller adds are there when the composition below reads them.
        configure?.Invoke(builder);

        UseStaticWebAssets(builder);

        ConfigureLogging(builder.Logging, logToStandardError: false);
        AddFluentCms(builder.Services, builder.Configuration, builder.Environment);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentInitiator, HttpContextInitiator>();
        builder.Services.AddScoped<IThemePreference, CookieThemePreference>();
        builder.Services.AddFluentCmsMcpHttp();
        var provider = AddCookieSignIn(builder.Services, builder.Configuration, builder.Environment);
        builder.Services.AddSignInRateLimit(builder.Configuration);
        builder.Services.AddHealthChecks();
        builder.Services.AddLocalization();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        RunJobsAsTheirInitiator(builder.Services);

        var app = builder.Build();
        app.UseSecurityHeaders(provider.IsConfigured ? [provider.AuthorityOrigin] : []);
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        KeepStatusCodePagesToPageLoads(app);
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        MapStaticAssets(app);
        app.MapHealthChecks("/health");
        app.MapFluentCmsMcp();
        app.MapFluentCmsApi();
        app.MapExternalSignIn();
        app.MapAuditExport();
        app.MapMediaLibrary();

        // Every admin page needs a signed-in user unless it allows anonymous access itself (setup, sign-in, not found).
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            .RequireAuthorization()
            .RateLimitCredentialForms();
        return app;
    }

    private static OpenIdConnectProviderOptions AddCookieSignIn(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var providerSection = configuration.GetSection(OpenIdConnectProviderOptions.SectionName);
        var provider = providerSection.Get<OpenIdConnectProviderOptions>() ?? new OpenIdConnectProviderOptions();
        services.Configure<OpenIdConnectProviderOptions>(providerSection);

        var authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies();
        authentication.AddFluentCmsAgentKeys();
        authentication.AddOpenIdConnectProvider(provider, environment);
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = AdminRoutes.SignIn;
            options.LogoutPath = AdminRoutes.SignOut;
        });
        // The data protection token provider for reset links is in the shared framework, which Infrastructure does not reference.
        new IdentityBuilder(typeof(FluentCmsUser), typeof(FluentCmsRole), services)
            .AddSignInManager()
            .AddTokenProvider<DataProtectorTokenProvider<FluentCmsUser>>(TokenOptions.DefaultProvider);
        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = configuration.GetValue(IdentityPasswordReset.LifespanSetting, TimeSpan.FromDays(1)));

        // Disabling a user changes their security stamp; checking it every minute instead of every thirty ends their session soon after.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));
        services.AddAuthorization();
        services.AddScoped<IUserSignIn, CookieUserSignIn>();
        services.AddScoped<IPasswordReset, IdentityPasswordReset>();
        return provider;
    }

    // The not-found page is for page loads. Re-running a rejected post against it would check the anti-forgery token
    // a second time, which throws once the first check has failed, and would call the rejected form "not found".
    private static void KeepStatusCodePagesToPageLoads(WebApplication app)
    {
        app.Use((context, next) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method)
                && !HttpMethods.IsHead(context.Request.Method)
                && context.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
            {
                statusCodePages.Enabled = false;
            }

            return next(context);
        });
    }

    // An unpublished build leaves the admin's files in its own wwwroot and lists them in this manifest, which the host defaults
    // load in Development alone and look for under the entry assembly's name, the test host under xunit. Without it the admin
    // is served without its stylesheet and script. A published build copies the files next to the binary and has no manifest.
    private static void UseStaticWebAssets(WebApplicationBuilder builder)
    {
        var manifest = Path.Combine(AppContext.BaseDirectory, "fluentcms.staticwebassets.runtime.json");

        if (File.Exists(manifest))
        {
            builder.WebHost.UseSetting(WebHostDefaults.StaticWebAssetsKey, manifest);
            builder.WebHost.UseStaticWebAssets();
        }
    }

    // The manifest is named after the entry assembly, which is the test host under xunit; without it the files are served unfingerprinted.
    private static void MapStaticAssets(WebApplication app)
    {
        var manifest = Path.Combine(AppContext.BaseDirectory, "fluentcms.staticwebassets.endpoints.json");

        if (File.Exists(manifest))
        {
            app.MapStaticAssets(manifest);
        }
        else
        {
            app.UseStaticFiles();
        }
    }

    public static IHost BuildMcpHost(
        string[] args,
        Stream? standardInput = null,
        Stream? standardOutput = null,
        Action<HostApplicationBuilder>? configure = null,
        string? agentKey = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        configure?.Invoke(builder);

        ConfigureLogging(builder.Logging, logToStandardError: true);
        AddFluentCms(builder.Services, builder.Configuration, builder.Environment);
        builder.Services.AddFluentCmsMcpStdio(agentKey ?? Environment.GetEnvironmentVariable(StdioAgentAuthentication.KeyVariable), standardInput, standardOutput);
        RunJobsAsTheirInitiator(builder.Services);

        return builder.Build();
    }

    // A job runs as whoever enqueued it, whatever the host's own caller is, so the initiator each host registered is wrapped; so is
    // the scope in which Core's engines work as themselves (ADR-0012).
    private static void RunJobsAsTheirInitiator(IServiceCollection services)
    {
        var registered = services.Last(descriptor => descriptor.ServiceType == typeof(ICurrentInitiator));
        services.Remove(registered);
        services.AddScoped<JobRun>();
        services.AddScoped<ICurrentInitiator>(provider => new JobAwareInitiator(
            provider.GetRequiredService<JobRun>(),
            registered.ImplementationInstance as ICurrentInitiator
                ?? registered.ImplementationFactory?.Invoke(provider) as ICurrentInitiator
                ?? (ICurrentInitiator)ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!)));
        services.AddEngineScopes();
    }

    // One composition for both entry modes, so the stdio host opens the same database and runs the same services.
    private static void AddFluentCms(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddFluentCmsCore();
        services.AddFluentCmsInfrastructure(configuration);
        services.AddFluentCmsApi(configuration);
        services.AddFluentCmsAdmin();

        // Both modes share one key ring, so whatever the web host protects the stdio host can read.
        var keyRing = configuration.GetSection(KeyRingOptions.SectionName).Get<KeyRingOptions>() ?? new KeyRingOptions();
        services.AddDataProtection()
            .SetApplicationName("FluentCMS")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(environment.ContentRootPath, keyRing.KeysDirectory)));

        // Media on local disk lives under the content root unless an absolute path is given, as the key ring does (MED-04).
        services.PostConfigure<LocalDiskOptions>(disk => disk.Root = Path.Combine(environment.ContentRootPath, disk.Root));

        services.Configure<JobQueueOptions>(configuration.GetSection(JobQueueOptions.SectionName));
        services.AddHostedService<JobQueueWorker>();
    }

    private static void ConfigureLogging(ILoggingBuilder logging, bool logToStandardError)
    {
        logging.ClearProviders();
        logging.AddJsonConsole();

        if (logToStandardError)
        {
            // Standard output belongs to the MCP transport, so every log line goes to standard error.
            logging.Services.Configure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        }
    }
}
