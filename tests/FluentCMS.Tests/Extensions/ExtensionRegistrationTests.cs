using FluentCMS.Core;
using FluentCMS.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Extensions;

public sealed class ExtensionRegistrationTests
{
    [Fact]
    public void Explicit_registration_resolves_and_is_catalogued()
    {
        var services = new ServiceCollection().AddFluentCmsExtension<IGreeting, HelloGreeting>();
        using var provider = services.BuildServiceProvider();

        Assert.Equal(["hello"], provider.GetServices<IGreeting>().Select(greeting => greeting.Text));

        var descriptor = Assert.Single(provider.GetRequiredService<IExtensionCatalog>().For<IGreeting>());
        Assert.Equal(typeof(HelloGreeting), descriptor.Implementation);
        Assert.False(descriptor.IsBuiltIn);
    }

    [Fact]
    public void Assembly_discovery_finds_every_implementation_of_an_extension_point_and_nothing_else()
    {
        var services = new ServiceCollection().AddFluentCmsExtensions(typeof(ExtensionRegistrationTests).Assembly);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(["hello", "hi"], provider.GetServices<IGreeting>().Select(greeting => greeting.Text).Order());
        Assert.Empty(provider.GetServices<IPlainService>());
        Assert.All(
            provider.GetRequiredService<IExtensionCatalog>().All,
            descriptor => Assert.True(descriptor.Contract.IsDefined(typeof(ExtensionPointAttribute), inherit: false)));
    }

    [Fact]
    public void Discovery_after_an_explicit_registration_does_not_duplicate()
    {
        var services = new ServiceCollection()
            .AddFluentCmsExtension<IGreeting, HelloGreeting>()
            .AddFluentCmsExtensions(typeof(ExtensionRegistrationTests).Assembly);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(2, provider.GetServices<IGreeting>().Count());
        Assert.Equal(2, provider.GetRequiredService<IExtensionCatalog>().For<IGreeting>().Count);
    }

    [Fact]
    public void A_contract_without_the_attribute_is_rejected()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddFluentCmsExtension<IPlainService, PlainService>());

        Assert.Contains("[ExtensionPoint]", exception.Message);
    }

    [Fact]
    public void Core_registers_its_built_ins_through_the_catalog()
    {
        using var provider = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();

        var catalog = provider.GetRequiredService<IExtensionCatalog>().All;
        Assert.NotEmpty(catalog);
        Assert.All(catalog, descriptor => Assert.True(descriptor.IsBuiltIn));
    }
}

[ExtensionPoint]
public interface IGreeting
{
    string Text { get; }
}

public sealed class HelloGreeting : IGreeting
{
    public string Text => "hello";
}

public sealed class HiGreeting : IGreeting
{
    public string Text => "hi";
}

public interface IPlainService;

public sealed class PlainService : IPlainService;
