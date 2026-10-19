using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Identity;

public static class EngineScopes
{
    // Wraps the ICurrentInitiator a host registered last, so Core's engines can do their own work inside a request as themselves,
    // such as the check run a submission starts (ADR-0012). A host calls it after registering its own. Only Core opens such a
    // scope, and only as an engine, so no other code can act as someone it is not.
    public static IServiceCollection AddEngineScopes(this IServiceCollection services)
    {
        var registered = services.Last(descriptor => descriptor.ServiceType == typeof(ICurrentInitiator));
        services.Remove(registered);
        services.AddScoped<EngineScope>();
        services.AddScoped<ICurrentInitiator>(provider => new EngineAwareInitiator(
            provider.GetRequiredService<EngineScope>(),
            registered.ImplementationInstance as ICurrentInitiator
                ?? registered.ImplementationFactory?.Invoke(provider) as ICurrentInitiator
                ?? (ICurrentInitiator)ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!)));

        return services;
    }

    internal static AsyncServiceScope CreateEngineScope(this IServiceScopeFactory scopes, SystemInitiator engine)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(engine);

        var scope = scopes.CreateAsyncScope();
        var acting = scope.ServiceProvider.GetService<EngineScope>()
            ?? throw new InvalidOperationException("The host does not let an engine act as itself inside a request; it calls AddEngineScopes.");
        acting.Engine = engine;
        return scope;
    }

    private sealed class EngineScope
    {
        public SystemInitiator? Engine { get; set; }
    }

    private sealed class EngineAwareInitiator(EngineScope scope, ICurrentInitiator host) : ICurrentInitiator
    {
        public Initiator? Current => scope.Engine ?? host.Current;
    }
}
