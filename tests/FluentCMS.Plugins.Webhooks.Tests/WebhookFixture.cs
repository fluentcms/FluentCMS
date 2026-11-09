using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.Plugins.Webhooks.Tests.WebhookTesting;

namespace FluentCMS.Plugins.Webhooks.Tests;

// The host the tests of a class share, started on its first use. Before handing it to the next test, HostAsync clears what
// the last one left: its webhooks, which would get this test's events and hold its names, and the jobs still scheduled for
// them, which the clock, moved past the last retry, makes due and which then find their webhook gone. A test tells its rows
// apart from the others' by its own webhooks.
public class WebhookFixture : IAsyncDisposable
{
    private readonly FluentCmsTestHost _host;
    private bool _used;

    public WebhookFixture()
        : this(Host())
    {
    }

    protected WebhookFixture(FluentCmsTestHost host) => _host = host;

    public async Task<FluentCmsTestHost> HostAsync()
    {
        if (_used)
        {
            await using (var scope = _host.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>().Set<Webhook>().ExecuteDeleteAsync(Token);
            }

            _host.Time.Advance(TimeSpan.FromDays(2));
            await PumpAsync(_host);
        }

        _used = true;
        return _host;
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
