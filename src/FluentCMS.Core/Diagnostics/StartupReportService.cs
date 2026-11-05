using System.Text.Json;
using FluentCMS.Core.Events;
using FluentCMS.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Diagnostics;

// The report of this start, kept for the diagnostics endpoint. It is null until the host has made one.
internal sealed class StartupReportHolder
{
    private volatile StartupReport? _report;

    public StartupReport? Current => _report;

    public void Set(StartupReport? report) => _report = report;
}

internal static class StartupReportJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
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
            // The message can name a connection or a path, so the log gets it scrubbed and the report only the type.
            NotMade(logger, exception.GetType().Name, SecretScrubber.Of(exception.Message));
            holder.Set(factory.CreateDegraded(exception));
            return;
        }

        holder.Set(report);
        var json = JsonSerializer.Serialize(report, StartupReportJson.Options);

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
        Message = "The startup report could not be made ({ExceptionType}: {Message}); the host starts, and the diagnostics endpoint serves what is known without it.")]
    private static partial void NotMade(ILogger logger, string exceptionType, string message);

    [LoggerMessage(
        EventId = StartupReportEvents.InsecureDefault,
        EventName = "InsecureDefault",
        Level = LogLevel.Warning,
        Message = "Insecure default {Id}: {Message}")]
    private static partial void Insecure(ILogger logger, string id, string message);
}

internal static class StartupReportEvents
{
    public const int Report = 9130;
    public const int InsecureDefault = 9131;
    public const int NotMade = 9132;
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
