using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.Sample;

// What the plugins of a test host did, in order. A test registers one instance before AddFluentCms, so that plugins find
// it in ConfigureServices as well as in Configure.
public sealed class PluginCalls
{
    private readonly ConcurrentQueue<string> _calls = new();

    public IReadOnlyList<string> All => [.. _calls];

    public static PluginCalls? In(IServiceCollection services) =>
        services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(PluginCalls) && !descriptor.IsKeyedService)?.ImplementationInstance as PluginCalls;

    public void Add(string call) => _calls.Enqueue(call);
}
