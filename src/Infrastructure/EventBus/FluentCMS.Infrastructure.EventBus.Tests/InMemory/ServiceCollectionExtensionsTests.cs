namespace FluentCMS.Infrastructure.EventBus.Tests.InMemory;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInMemoryEventBus_ShouldThrowArgumentNullException_WhenServicesIsNull()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act
        var act = () => services!.AddInMemoryEventBus();

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddInMemoryEventBus_ShouldRegisterEventPublisher_AsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddInMemoryEventBus();

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventPublisher));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddInMemoryEventBus_ShouldRegisterHttpContextAccessor()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddInMemoryEventBus();

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IHttpContextAccessor));
        descriptor.Should().NotBeNull();
    }

    [Fact]
    public void AddInMemoryEventBus_ShouldConfigureOptions_WithDefaultSettings()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddInMemoryEventBus();
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<EventPublisherOptions>>();

        // Assert
        options.Value.Mode.Should().Be(EventPublisherOptions.ErrorHandlingMode.Aggregate);
    }

    [Fact]
    public void AddInMemoryEventBus_ShouldConfigureOptions_WithCustomSettings()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddInMemoryEventBus(options => 
        {
            options.Mode = EventPublisherOptions.ErrorHandlingMode.FailFast;
        });
        var provider = services.BuildServiceProvider();
        var optionsValue = provider.GetRequiredService<IOptions<EventPublisherOptions>>();

        // Assert
        optionsValue.Value.Mode.Should().Be(EventPublisherOptions.ErrorHandlingMode.FailFast);
    }

    [Fact]
    public void AddInMemoryEventBus_ShouldReturnServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddInMemoryEventBus();

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddInMemoryEventBus_ShouldAllowFluentConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services
            .AddInMemoryEventBus(options => options.Mode = EventPublisherOptions.ErrorHandlingMode.FailFast)
            .AddLogging();

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddInMemoryEventBus_ShouldNotAddDuplicatePublisher_WhenCalledMultipleTimes()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddInMemoryEventBus();
        services.AddInMemoryEventBus();

        // Assert
        var descriptors = services.Where(d => d.ServiceType == typeof(IEventPublisher)).ToList();
        descriptors.Should().HaveCount(1, "TryAddSingleton should prevent duplicates");
    }

    [Fact]
    public void AddInMemoryEventBus_ShouldResolveEventPublisher()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInMemoryEventBus();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        // Act
        var publisher = provider.GetService<IEventPublisher>();

        // Assert
        publisher.Should().NotBeNull();
        publisher.Should().BeAssignableTo<IEventPublisher>();
    }

    [Fact]
    public void AddInMemoryEventBus_WithNullConfiguration_ShouldUseDefaults()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddInMemoryEventBus(configure: null);
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<EventPublisherOptions>>();

        // Assert
        options.Value.Mode.Should().Be(EventPublisherOptions.ErrorHandlingMode.Aggregate);
    }
}
