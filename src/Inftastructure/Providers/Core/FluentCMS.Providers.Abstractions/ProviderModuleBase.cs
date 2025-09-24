using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Providers.Abstractions;

public abstract class ProviderModuleBase<TProvider, TOptions> : ProviderModuleBase<TProvider>
    where TProvider : class, IProvider
    where TOptions : class, new()
{
    public override Type? OptionsType => typeof(TOptions);
}

public abstract class ProviderModuleBase<TProvider> : IProviderModule<TProvider>
    where TProvider : class, IProvider
{
    public abstract string Area { get; }
    public abstract string DisplayName { get; }
    public Type ProviderType => typeof(TProvider);
    public virtual Type? OptionsType => null;

    public virtual Type InterfaceType
    {
        get
        {
            var interfaces = typeof(TProvider).GetInterfaces()
                .Where(i => i != typeof(IProvider) && typeof(IProvider).IsAssignableFrom(i))
                .ToArray();

            if (interfaces.Length == 0)
                throw new InvalidOperationException($"Provider {typeof(TProvider).Name} must implement at least one interface that extends IProvider.");

            // Return the most specific interface (first one that's not IProvider)
            return interfaces.First();
        }
    }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        // Default implementation does nothing
        // Override in derived classes to register additional services
    }
}
