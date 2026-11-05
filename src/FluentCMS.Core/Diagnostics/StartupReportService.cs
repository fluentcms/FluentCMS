using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Diagnostics;

// The report of this start, kept for the diagnostics endpoint.
internal sealed class StartupReportHolder
{
    private volatile StartupReport? _report;

    public StartupReport Report => _report ?? throw new InvalidOperationException("The startup report is made when the host has started.");

    public void Set(StartupReport report) => _report = report;
}

// Runs after every StartingAsync, which is where the migrations are, so the report sees the database as the host will use it.
internal sealed partial class StartupReportService(StartupReportFactory factory, StartupReportHolder holder, ILogger<StartupReportService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        StartupReport report;
        try
        {
            report = await factory.CreateAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            NotMade(logger, exception.GetType().Name, exception);
            return;
        }

        holder.Set(report);
        var json = JsonSerializer.Serialize(report, StartupReport.Json);

        Started(
            logger,
            report.Version,
            report.Environment,
            report.DataProvider.Provider,
            report.DataProvider.ProviderVersion,
            report.PendingMigrations.Count,
            report.Schema.Drifted,
            report.InsecureDefaults.Count,
            json);

        foreach (var item in report.InsecureDefaults)
        {
            Insecure(logger, item.Id, item.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = StartupReportEvents.Report,
        EventName = "StartupReport",
        Level = LogLevel.Information,
        Message = "FluentCMS {Version} started in {Environment} on {Provider} {ProviderVersion}: {PendingMigrations} pending migrations, schema drift {SchemaDrift}, {InsecureDefaults} insecure defaults. Report: {Report}")]
    private static partial void Started(ILogger logger, string version, string environment, string provider, string providerVersion, int pendingMigrations, bool schemaDrift, int insecureDefaults, string report);

    [LoggerMessage(
        EventId = StartupReportEvents.NotMade,
        EventName = "StartupReportNotMade",
        Level = LogLevel.Warning,
        Message = "The startup report could not be made ({ExceptionType}); the host starts without it.")]
    private static partial void NotMade(ILogger logger, string exceptionType, Exception exception);

    [LoggerMessage(
        EventId = StartupReportEvents.InsecureDefault,
        EventName = "InsecureDefault",
        Level = LogLevel.Warning,
        Message = "Insecure default {Id}: {Message}")]
    private static partial void Insecure(ILogger logger, string id, string message);
}

internal static class StartupReportEvents
{
    public const int Report = 1100;
    public const int InsecureDefault = 1101;
    public const int NotMade = 1102;
}

internal static class StartupReportRegistration
{
    public static void AddStartupReport(this IServiceCollection services)
    {
        services.AddSingleton<StartupReportHolder>();
        services.AddSingleton<StartupReportFactory>();
        services.AddHostedService<StartupReportService>();
    }
}
