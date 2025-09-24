using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Providers.Abstractions;

public interface IProviderModule
{
    string Area { get; }
    string DisplayName { get; }
    Type ProviderType { get; }
    Type? OptionsType { get; }
    Type InterfaceType { get; }
    void ConfigureServices(IServiceCollection services);
}


/// <summary>
/// Generic interface for strongly-typed provider modules.
/// </summary>
/// <typeparam name="TProvider">The provider implementation type.</typeparam>
/// <typeparam name="TOptions">The options type for the provider.</typeparam>
public interface IProviderModule<TProvider, TOptions> : IProviderModule
    where TProvider : class, IProvider
    where TOptions : class, new()
{
}

public interface IProviderModule<TProvider> : IProviderModule
    where TProvider : class, IProvider
{
}
