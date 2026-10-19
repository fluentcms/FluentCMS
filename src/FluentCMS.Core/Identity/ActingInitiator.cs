using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Identity;

// The initiator a scope acts as instead of the host's caller: a job's, or an engine's when the product does work on its own inside
// a request, such as the check run a submission starts (ADR-0012). Null leaves the host's caller.
public sealed class ActingInitiator
{
    public Initiator? Initiator { get; set; }
}

public static class ActingInitiatorExtensions
{
    // Wraps the ICurrentInitiator a host registered last, so a scope can act as another initiator. A host calls it after
    // registering its own.
    public static IServiceCollection AddActingInitiator(this IServiceCollection services)
    {
        var registered = services.Last(descriptor => descriptor.ServiceType == typeof(ICurrentInitiator));
        services.Remove(registered);
        services.AddScoped<ActingInitiator>();
        services.AddScoped<ICurrentInitiator>(provider => new ActingCurrentInitiator(
            provider.GetRequiredService<ActingInitiator>(),
            registered.ImplementationInstance as ICurrentInitiator
                ?? registered.ImplementationFactory?.Invoke(provider) as ICurrentInitiator
                ?? (ICurrentInitiator)ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!)));

        return services;
    }

    // A scope of its own that acts as the initiator given, whoever the host's caller is.
    public static AsyncServiceScope CreateScopeActingAs(this IServiceScopeFactory scopes, Initiator? initiator)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var scope = scopes.CreateAsyncScope();
        var acting = scope.ServiceProvider.GetService<ActingInitiator>()
            ?? throw new InvalidOperationException("The host does not let a scope act as another initiator; it calls AddActingInitiator.");
        acting.Initiator = initiator;
        return scope;
    }

    private sealed class ActingCurrentInitiator(ActingInitiator acting, ICurrentInitiator host) : ICurrentInitiator
    {
        public Initiator? Current => acting.Initiator ?? host.Current;
    }
}
