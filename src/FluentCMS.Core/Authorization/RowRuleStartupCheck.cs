using Microsoft.Extensions.Hosting;

namespace FluentCMS.Core.Authorization;

// Taking the registry builds it, so that a rule that does not fit the schema stops the host, not the first request.
internal sealed class RowRuleStartupCheck : IHostedService
{
    public RowRuleStartupCheck(RowRuleRegistry registry) => ArgumentNullException.ThrowIfNull(registry);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
