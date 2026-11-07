using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Core.DependencyInjection;

// A setting that is safe in development and not in Production. Null lets the host start.
internal interface IProductionStartupCheck
{
    StartupRefusal? Check();
}

internal sealed record StartupRefusal(string Code, string Message);

// Runs while the host is starting, before the migrations, so that a refused host never touches the database.
internal sealed class ProductionStartupChecks(IEnumerable<IProductionStartupCheck> checks, IHostEnvironment? environment = null) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (environment?.IsProduction() != true)
        {
            return Task.CompletedTask;
        }

        List<Exception> refusals = [.. checks
            .Select(check => check.Check())
            .OfType<StartupRefusal>()
            .Select(refusal => new FluentCmsConfigurationException(refusal.Code, refusal.Message))];

        return refusals.Count switch
        {
            0 => Task.CompletedTask,
            1 => throw refusals[0],
            _ => throw new AggregateException("FluentCMS refuses to start in Production.", refusals),
        };
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal static class ProductionStartupCheckRegistration
{
    // Lifecycle services start in the order they are registered, so this goes before MigrationStartupService.
    public static void AddProductionStartupChecks(this IServiceCollection services)
    {
        services.AddHostedService<ProductionStartupChecks>();
        services.AddSingleton<IProductionStartupCheck, GeneratedSigningKeyCheck>();
        services.AddSingleton<IProductionStartupCheck, PublicOriginCheck>();
        services.AddSingleton<IProductionStartupCheck, UnencryptedKeyRingCheck>();
    }
}
