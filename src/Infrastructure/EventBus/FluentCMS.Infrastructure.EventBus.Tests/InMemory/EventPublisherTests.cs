namespace FluentCMS.Infrastructure.EventBus.Tests.InMemory;

public class EventPublisherTests
{
    private class TestEvent : EventBase
    {
        public string Message { get; set; } = string.Empty;
    }

    [Fact]
    public async Task Publish_ShouldThrowArgumentNullException_WhenEventIsNull()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInMemoryEventBus();
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IEventPublisher>();

        // Act
        var act = async () => await publisher.Publish<TestEvent>(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Publish_ShouldThrowOperationCanceledException_WhenCancellationRequested()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInMemoryEventBus();
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IEventPublisher>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var @event = new TestEvent { Message = "Test" };

        // Act
        var act = async () => await publisher.Publish(@event, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Publish_ShouldInvokeSubscriber_WhenOneSubscriberExists()
    {
        // Arrange
        var handlerCalled = false;
        var services = new ServiceCollection();
        services.AddInMemoryEventBus();
        services.AddLogging();
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new TestEventHandler(() => handlerCalled = true));

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IEventPublisher>();

        var @event = new TestEvent { Message = "Test" };

        // Act
        await publisher.Publish(@event);

        // Assert
        handlerCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Publish_ShouldInvokeAllSubscribers_WhenMultipleSubscribersExist()
    {
        // Arrange
        var handler1Called = false;
        var handler2Called = false;
        var services = new ServiceCollection();
        services.AddInMemoryEventBus();
        services.AddLogging();
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new TestEventHandler(() => handler1Called = true));
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new TestEventHandler(() => handler2Called = true));

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IEventPublisher>();

        var @event = new TestEvent { Message = "Test" };

        // Act
        await publisher.Publish(@event);

        // Assert
        handler1Called.Should().BeTrue();
        handler2Called.Should().BeTrue();
    }

    [Fact]
    public async Task Publish_FailFastMode_ShouldStopOnFirstException()
    {
        // Arrange
        var handler1Called = false;
        var handler2Called = false;
        var services = new ServiceCollection();
        services.AddInMemoryEventBus(options =>
        {
            options.Mode = EventPublisherOptions.ErrorHandlingMode.FailFast;
        });
        services.AddLogging();
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new TestEventHandler(() =>
        {
            handler1Called = true;
            throw new InvalidOperationException("Handler 1 failed");
        }));
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new TestEventHandler(() => handler2Called = true));

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IEventPublisher>();

        var @event = new TestEvent { Message = "Test" };

        // Act
        var act = async () => await publisher.Publish(@event);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Handler 1 failed");

        handler1Called.Should().BeTrue();
        handler2Called.Should().BeFalse();
    }

    [Fact]
    public async Task Publish_AggregateMode_ShouldCollectAllExceptions()
    {
        // Arrange
        var handler1Called = false;
        var handler2Called = false;
        var services = new ServiceCollection();
        services.AddInMemoryEventBus(options =>
        {
            options.Mode = EventPublisherOptions.ErrorHandlingMode.Aggregate;
        });
        services.AddLogging();
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new TestEventHandler(() =>
        {
            handler1Called = true;
            throw new InvalidOperationException("Handler 1 failed");
        }));
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new TestEventHandler(() =>
        {
            handler2Called = true;
            throw new InvalidOperationException("Handler 2 failed");
        }));

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IEventPublisher>();

        var @event = new TestEvent { Message = "Test" };

        // Act
        var act = async () => await publisher.Publish(@event);

        // Assert
        var aggregateException = await act.Should().ThrowAsync<EventPublisherAggregatedException<TestEvent>>();
        aggregateException.Which.InnerExceptions.Should().HaveCount(2);
        aggregateException.Which.InnerExceptions.Should().Contain(e => e.Message == "Handler 1 failed");
        aggregateException.Which.InnerExceptions.Should().Contain(e => e.Message == "Handler 2 failed");

        handler1Called.Should().BeTrue();
        handler2Called.Should().BeTrue();
    }

    [Fact]
    public async Task Publish_ShouldUseHttpContext_WhenAvailable()
    {
        // Arrange
        var handlerCalled = false;
        var services = new ServiceCollection();
        services.AddInMemoryEventBus();
        services.AddLogging();
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new TestEventHandler(() => handlerCalled = true));

        // Create HTTP context with its own service provider
        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services.BuildServiceProvider();

        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Override the IHttpContextAccessor registration
        services.AddSingleton(httpContextAccessorMock.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IEventPublisher>();

        var @event = new TestEvent { Message = "Test" };

        // Act
        await publisher.Publish(@event);

        // Assert
        handlerCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Publish_FailFastMode_ShouldExecuteSequentially()
    {
        // Arrange
        var executionOrder = new List<int>();
        var lockObject = new object();
        var services = new ServiceCollection();
        services.AddInMemoryEventBus(options =>
        {
            options.Mode = EventPublisherOptions.ErrorHandlingMode.FailFast;
        });
        services.AddLogging();
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new TestEventHandler(async () =>
        {
            await Task.Delay(50);
            lock (lockObject) { executionOrder.Add(1); }
        }));
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new TestEventHandler(() =>
        {
            lock (lockObject) { executionOrder.Add(2); }
        }));

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IEventPublisher>();

        var @event = new TestEvent { Message = "Test" };

        // Act
        await publisher.Publish(@event);

        // Assert
        executionOrder.Should().Equal(new[] { 1, 2 });
    }

    [Fact]
    public async Task Publish_AggregateMode_ShouldExecuteConcurrently()
    {
        // Arrange
        var handler1Started = false;
        var handler2Started = false;
        var services = new ServiceCollection();
        services.AddInMemoryEventBus(options =>
        {
            options.Mode = EventPublisherOptions.ErrorHandlingMode.Aggregate;
        });
        services.AddLogging();
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new TestEventHandler(async () =>
        {
            handler1Started = true;
            await Task.Delay(100);
        }));
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new TestEventHandler(async () =>
        {
            handler2Started = true;
            await Task.Delay(50);
        }));

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IEventPublisher>();

        var @event = new TestEvent { Message = "Test" };

        // Act
        await publisher.Publish(@event);

        // Assert - both handlers should have started (indicating concurrent execution)
        handler1Started.Should().BeTrue();
        handler2Started.Should().BeTrue();
    }

    [Fact]
    public async Task Publish_ShouldPassEventDataToHandler()
    {
        // Arrange
        TestEvent? receivedEvent = null;
        var services = new ServiceCollection();
        services.AddInMemoryEventBus();
        services.AddLogging();
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new TestEventHandler(e => receivedEvent = e));

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IEventPublisher>();

        var @event = new TestEvent { Message = "Test Message" };

        // Act
        await publisher.Publish(@event);

        // Assert
        receivedEvent.Should().NotBeNull();
        receivedEvent!.Message.Should().Be("Test Message");
        receivedEvent.EventId.Should().Be(@event.EventId);
        receivedEvent.OccurredAt.Should().Be(@event.OccurredAt);
    }

    [Fact]
    public async Task Publish_ShouldPassCancellationToken_ToHandlers()
    {
        // Arrange
        CancellationToken receivedToken = default;
        var services = new ServiceCollection();
        services.AddInMemoryEventBus();
        services.AddLogging();
        services.AddScoped<IEventSubscriber<TestEvent>>(_ => new TestEventHandler((e, ct) => receivedToken = ct));

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IEventPublisher>();

        var @event = new TestEvent { Message = "Test" };
        using var cts = new CancellationTokenSource();

        // Act
        await publisher.Publish(@event, cts.Token);

        // Assert
        receivedToken.Should().Be(cts.Token);
    }

    // Helper class for testing
    private class TestEventHandler : IEventSubscriber<TestEvent>
    {
        private readonly Func<Task>? _asyncAction;
        private readonly Action? _action;
        private readonly Action<TestEvent>? _eventAction;
        private readonly Action<TestEvent, CancellationToken>? _eventCancellableAction;

        public TestEventHandler(Func<Task> asyncAction)
        {
            _asyncAction = asyncAction;
        }

        public TestEventHandler(Action action)
        {
            _action = action;
        }

        public TestEventHandler(Action<TestEvent> eventAction)
        {
            _eventAction = eventAction;
        }

        public TestEventHandler(Action<TestEvent, CancellationToken> eventCancellableAction)
        {
            _eventCancellableAction = eventCancellableAction;
        }

        public async Task Handle(TestEvent domainEvent, CancellationToken cancellationToken = default)
        {
            if (_asyncAction != null)
                await _asyncAction();
            else if (_action != null)
                _action();
            else if (_eventAction != null)
                _eventAction(domainEvent);
            else if (_eventCancellableAction != null)
                _eventCancellableAction(domainEvent, cancellationToken);
        }
    }
}
