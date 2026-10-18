using FluentCMS.Admin;
using FluentCMS.Admin.Components;
using FluentCMS.Api;
using FluentCMS.Core;
using FluentCMS.Core.Identity;
using FluentCMS.Host.Identity;
using FluentCMS.Host.Jobs;
using FluentCMS.Host.Security;
using FluentCMS.Infrastructure;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.Mcp;
using Microsoft.AspNetCore.DataProtection;
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

        ConfigureLogging(builder.Logging, logToStandardError: false);
        AddFluentCms(builder.Services, builder.Configuration, builder.Environment);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentInitiator, HttpContextInitiator>();
        AddCookieSignIn(builder.Services);
        builder.Services.AddSignInRateLimit(builder.Configuration);
        builder.Services.AddHealthChecks();
        builder.Services.AddLocalization();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        var app = builder.Build();
        app.UseSecurityHeaders();
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        MapStaticAssets(app);
        app.MapHealthChecks("/health");

        // Every admin page needs a signed-in user unless it allows anonymous access itself (setup, sign-in, not found).
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            .RequireAuthorization()
            .RateLimitCredentialForms();
        return app;
    }

    private static void AddCookieSignIn(IServiceCollection services)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = AdminRoutes.SignIn;
            options.LogoutPath = AdminRoutes.SignOut;
        });
        new IdentityBuilder(typeof(FluentCmsUser), typeof(FluentCmsRole), services).AddSignInManager();
        services.AddAuthorization();
        services.AddScoped<IUserSignIn, CookieUserSignIn>();
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

    public static IHost BuildMcpHost(string[] args, Stream? standardInput = null, Action<HostApplicationBuilder>? configure = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        configure?.Invoke(builder);

        ConfigureLogging(builder.Logging, logToStandardError: true);
        AddFluentCms(builder.Services, builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(standardInput is null
            ? StandardInputSource.Console
            : new StandardInputSource(() => standardInput));
        builder.Services.AddHostedService<StandardInputWatcher>();
        builder.Services.AddScoped<ICurrentInitiator, NoInitiator>();

        return builder.Build();
    }

    // One composition for both entry modes, so the stdio host opens the same database and runs the same services.
    private static void AddFluentCms(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddFluentCmsCore();
        services.AddFluentCmsInfrastructure(configuration);
        services.AddFluentCmsMcp();
        services.AddFluentCmsApi();
        services.AddFluentCmsAdmin(options => options.ShowcaseEnabled = environment.IsDevelopment());

        // Both modes share one key ring, so whatever the web host protects the stdio host can read.
        var keyRing = configuration.GetSection(KeyRingOptions.SectionName).Get<KeyRingOptions>() ?? new KeyRingOptions();
        services.AddDataProtection()
            .SetApplicationName("FluentCMS")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(environment.ContentRootPath, keyRing.KeysDirectory)));

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
