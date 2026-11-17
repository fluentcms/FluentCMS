using FluentCMS.Admin.Lists;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Admin.Plugins;

/// <summary>
/// The admin half of a plugin: a class with a public parameterless constructor that carries
/// <see cref="FluentCmsAdminPluginAttribute"/> and is named by the attribute of its assembly.
/// </summary>
public interface IAdminPlugin
{
    /// <summary>Configures the half. It runs once, when the assembly of the half is first loaded.</summary>
    /// <param name="admin">The builder of the half.</param>
    void Configure(IAdminPluginBuilder admin);
}

/// <summary>What an admin half configures when its assembly is first loaded.</summary>
public interface IAdminPluginBuilder
{
    /// <summary>Gets the name of the server plugin.</summary>
    string Name { get; }

    /// <summary>
    /// Gets the services of the half. They are built into a provider of the half that falls back to the admin's services,
    /// which the half's components reach through <see cref="IAdminPluginServices"/>; the admin's own container takes none.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>Gets the filters of the generated lists, to which the half adds the operators of its field types.</summary>
    ListFilterRegistry Filters { get; }
}

/// <summary>
/// Gives a component the services of admin halves. The admin's container is built before a half is loaded, so a half's
/// services are in a provider of its own.
/// </summary>
public interface IAdminPluginServices
{
    /// <summary>The provider of a loaded half, which falls back to the admin's services.</summary>
    /// <param name="type">A type of the half's assembly, such as the calling component or the plugin's class.</param>
    /// <returns>The provider of the half.</returns>
    /// <exception cref="InvalidOperationException">The assembly of <paramref name="type"/> is not a loaded admin half.</exception>
    IServiceProvider ProviderOf(Type type);

    /// <summary>
    /// Finds a service that a half provides with <see cref="AdminServiceAttribute"/>, such as the media picker, and loads
    /// that half first when it is not loaded.
    /// </summary>
    /// <typeparam name="TService">The service, an interface both halves know, such as one of <c>FluentCMS.Admin.Abstractions</c>.</typeparam>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The service, or <see langword="null"/> when no half that is enabled in the site provides it.</returns>
    ValueTask<TService?> FindAsync<TService>(CancellationToken cancellationToken = default)
        where TService : class;
}
