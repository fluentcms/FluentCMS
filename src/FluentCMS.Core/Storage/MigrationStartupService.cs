using FluentCMS.Core.Commands;
using FluentCMS.Core.Plugins;
using FluentCMS.Schema;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Core.Storage;

// Runs in the Starting phase, so the database is ready before any hosted service starts and a failure stops the host.
internal sealed class MigrationStartupService(MigrationRunner runner, SchemaModel schema, PluginRegistry? plugins = null, ProcessArguments? arguments = null) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        CommandEntryPointCheck.Refuse(arguments);
        plugins?.Contributions.CheckContentTables(schema);
        return runner.RunAtStartupAsync(cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
