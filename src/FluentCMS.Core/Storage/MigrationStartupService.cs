using Microsoft.Extensions.Hosting;

namespace FluentCMS.Core.Storage;

// Runs in the Starting phase, so the database is ready before any hosted service starts and a failure stops the host.
internal sealed class MigrationStartupService(MigrationRunner runner) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => runner.RunAtStartupAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
