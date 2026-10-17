using FluentCMS.Admin;
using FluentCMS.Admin.Components;
using FluentCMS.Api;
using FluentCMS.Core;
using FluentCMS.Core.Identity;
using FluentCMS.Host.Identity;
using FluentCMS.Host.Jobs;
using FluentCMS.Infrastructure;
using FluentCMS.Mcp;
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

        ConfigureLogging(builder.Logging, logToStandardError: false);
        AddFluentCms(builder.Services, builder.Configuration, builder.Environment);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentInitiator, HttpContextInitiator>();
        builder.Services.AddHealthChecks();
        builder.Services.AddLocalization();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        configure?.Invoke(builder);

        var app = builder.Build();
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapHealthChecks("/health");
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        return app;
    }

    public static IHost BuildMcpHost(string[] args, Stream? standardInput = null, Action<HostApplicationBuilder>? configure = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);

        ConfigureLogging(builder.Logging, logToStandardError: true);
        AddFluentCms(builder.Services, builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(standardInput is null
            ? StandardInputSource.Console
            : new StandardInputSource(() => standardInput));
        builder.Services.AddHostedService<StandardInputWatcher>();
        builder.Services.AddScoped<ICurrentInitiator, NoInitiator>();
        configure?.Invoke(builder);

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
