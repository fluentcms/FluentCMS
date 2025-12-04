namespace FluentCMS.Infrastructure.EventBus.Tests;

public class ServiceCollectionExtensionsTests
{
    private class TestEvent : EventBase
    {
        public string Message { get; set; } = string.Empty;
    }

    private class TestEventHandler : IEventSubscriber<TestEvent>
    {
        public Task Handle(TestEvent domainEvent, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private class AnotherTestEventHandler : IEventSubscriber<TestEvent>
    {
        public Task Handle(TestEvent domainEvent, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void AddEventHandler_ShouldThrowArgumentNullException_WhenServicesIsNull()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act
        var act = () => services!.AddEventHandler<TestEvent, TestEventHandler>();

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddEventHandler_ShouldRegisterHandler_AsScoped()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddEventHandler<TestEvent, TestEventHandler>();

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventSubscriber<TestEvent>));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        descriptor.ImplementationType.Should().Be(typeof(TestEventHandler));
    }

    [Fact]
    public void AddEventHandler_ShouldReturnServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddEventHandler<TestEvent, TestEventHandler>();

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddEventHandler_ShouldAllowMultipleHandlersForSameEvent()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddEventHandler<TestEvent, TestEventHandler>();
        services.AddEventHandler<TestEvent, AnotherTestEventHandler>();

        // Assert
        var descriptors = services.Where(d => d.ServiceType == typeof(IEventSubscriber<TestEvent>)).ToList();
        descriptors.Should().HaveCount(2);
        descriptors[0].ImplementationType.Should().Be(typeof(TestEventHandler));
        descriptors[1].ImplementationType.Should().Be(typeof(AnotherTestEventHandler));
    }

    [Fact]
    public void AddEventHandler_ShouldResolveHandlerFromServiceProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddEventHandler<TestEvent, TestEventHandler>();
        var provider = services.BuildServiceProvider();

        // Act
        using var scope = provider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IEventSubscriber<TestEvent>>();

        // Assert
        handlers.Should().HaveCount(1);
        handlers.First().Should().BeOfType<TestEventHandler>();
    }
}
