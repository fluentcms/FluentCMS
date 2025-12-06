namespace FluentCMS.Infrastructure.EventBus.Tests.InMemory;

public class EventPublisherAggregatedExceptionTests
{
    private class TestEvent : EventBase
    {
        public string Message { get; set; } = string.Empty;
    }

    [Fact]
    public void Constructor_ShouldSetInnerExceptions()
    {
        // Arrange
        var exception1 = new InvalidOperationException("Error 1");
        var exception2 = new ArgumentException("Error 2");
        var innerExceptions = new Exception[] { exception1, exception2 };

        // Act
        var aggregatedException = new EventPublisherAggregatedException<TestEvent>(innerExceptions);

        // Assert
        aggregatedException.InnerExceptions.Should().HaveCount(2);
        aggregatedException.InnerExceptions.Should().Contain(exception1);
        aggregatedException.InnerExceptions.Should().Contain(exception2);
    }

    [Fact]
    public void Constructor_ShouldSetEventType()
    {
        // Arrange
        var exception1 = new InvalidOperationException("Error 1");
        var innerExceptions = new[] { exception1 };

        // Act
        var aggregatedException = new EventPublisherAggregatedException<TestEvent>(innerExceptions);

        // Assert
        aggregatedException.EventType.Should().Be(typeof(TestEvent));
    }

    [Fact]
    public void Constructor_ShouldSetMessage_WithEventTypeName()
    {
        // Arrange
        var exception1 = new InvalidOperationException("Error 1");
        var innerExceptions = new[] { exception1 };

        // Act
        var aggregatedException = new EventPublisherAggregatedException<TestEvent>(innerExceptions);

        // Assert
        aggregatedException.Message.Should().Contain("TestEvent");
        aggregatedException.Message.Should().Contain("event handlers threw an exception");
    }

    [Fact]
    public void Constructor_ShouldBeAssignableToAggregateException()
    {
        // Arrange
        var exception1 = new InvalidOperationException("Error 1");
        var innerExceptions = new[] { exception1 };

        // Act
        var aggregatedException = new EventPublisherAggregatedException<TestEvent>(innerExceptions);

        // Assert
        aggregatedException.Should().BeAssignableTo<AggregateException>();
    }

    [Fact]
    public void Constructor_ShouldHandleMultipleExceptions()
    {
        // Arrange
        var exception1 = new InvalidOperationException("Error 1");
        var exception2 = new ArgumentException("Error 2");
        var exception3 = new NotSupportedException("Error 3");
        var innerExceptions = new Exception[] { exception1, exception2, exception3 };

        // Act
        var aggregatedException = new EventPublisherAggregatedException<TestEvent>(innerExceptions);

        // Assert
        aggregatedException.InnerExceptions.Should().HaveCount(3);
        aggregatedException.InnerExceptions.Should().ContainInOrder(exception1, exception2, exception3);
    }

    [Fact]
    public void EventType_ShouldBeGenericType()
    {
        // Arrange
        var exception1 = new InvalidOperationException("Error 1");
        var innerExceptions = new[] { exception1 };

        // Act
        var aggregatedException = new EventPublisherAggregatedException<TestEvent>(innerExceptions);

        // Assert
        aggregatedException.EventType.Should().Be(typeof(TestEvent));
        aggregatedException.EventType.Name.Should().Be("TestEvent");
    }

    [Fact]
    public void Constructor_WithEmptyCollection_ShouldCreateException()
    {
        // Arrange
        var innerExceptions = Array.Empty<Exception>();

        // Act
        var aggregatedException = new EventPublisherAggregatedException<TestEvent>(innerExceptions);

        // Assert
        aggregatedException.InnerExceptions.Should().BeEmpty();
        aggregatedException.EventType.Should().Be(typeof(TestEvent));
    }
}
