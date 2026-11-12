using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Events;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage;
using FluentCMS.Diagnostics;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Commands;

// The startup report of 4.15 without starting the host: it runs before any hosted service, so nothing listens, migrates or
// writes, and it reads the database only for the pending migrations and the schema history. It also asks what would stop
// the host from starting: a schema with errors, a Production check that refuses, and a migration set of another provider.
internal sealed class DoctorCommand : HostCommand
{
    public const string Json = "--json";

    public override string Name => "doctor";

    public override string Description => "Prints the startup report without starting the host; exits with 1 when the host would not start or a migration is pending.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Json, "Writes the report as one line of JSON, in the shape of GET /system/diagnostics."),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var services = context.Services;
        try
        {
            _ = services.GetRequiredService<SchemaModel>();
        }
        catch (SchemaValidationException exception)
        {
            // Every part of the report needs the schema, so there is none.
            await context.Error.WriteLineAsync($"{CommandErrorCodes.HostWouldNotStart}: the host would not start: {exception.Message}");
            return CommandExitCodes.Failure;
        }

        var refusals = new List<string>();
        if (services.GetService<IHostEnvironment>()?.IsProduction() == true)
        {
            refusals.AddRange(services.GetServices<IProductionStartupCheck>().Select(Refusal).OfType<string>());
        }

        try
        {
            await services.GetRequiredService<MigrationRunner>().RefuseForeignMigrationsAsync(cancellationToken);
        }
        catch (FluentCmsConfigurationException exception)
        {
            refusals.Add(exception.Message);
        }

        var factory = services.GetRequiredService<StartupReportFactory>();
        StartupReport report;
        try
        {
            report = await factory.CreateAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The report then says only the type; the log keeps the exception, scrubbed.
            CommandLog.Failed(services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DoctorCommand).FullName!), Name, exception.GetType().Name, SecretScrubber.Of(exception.ToString()));
            report = factory.CreateDegraded(exception);
        }

        return await DoctorReport.WriteAsync(report, refusals, context.Arguments.Has(Json), context.Output, context.Error);
    }

    // A check that throws stops the start as a refusal does.
    private static string? Refusal(IProductionStartupCheck check)
    {
        try
        {
            return check.Check() is { } refusal ? $"{refusal.Code}: {refusal.Message}" : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return $"a startup check failed ({exception.GetType().Name}: {SecretScrubber.Of(exception.Message)})";
        }
    }
}
