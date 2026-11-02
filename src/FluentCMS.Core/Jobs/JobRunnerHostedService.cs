using FluentCMS.Jobs;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Core.Jobs;

// Runs whichever IJobRunner is registered. MigrationStartupService has finished by the time this starts.
internal sealed class JobRunnerHostedService(IJobRunner runner) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => runner.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => runner.StopAsync(cancellationToken);
}
