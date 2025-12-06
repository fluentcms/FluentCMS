namespace FluentCMS.Infrastructure.EventBus.Tests;

public class EventBaseTests
{
    private class TestEvent : EventBase
    {
        public string Message { get; set; } = string.Empty;
    }

    [Fact]
    public void Constructor_ShouldSetOccurredAt()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var @event = new TestEvent();

        // Assert
        var after = DateTimeOffset.UtcNow;
        @event.OccurredAt.Should().BeOnOrAfter(before);
        @event.OccurredAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void Constructor_ShouldSetEventId()
    {
        // Act
        var @event = new TestEvent();

        // Assert
        @event.EventId.Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_ShouldGenerateUniqueEventIds()
    {
        // Act
        var event1 = new TestEvent();
        var event2 = new TestEvent();

        // Assert
        event1.EventId.Should().NotBe(event2.EventId);
    }

    [Fact]
    public void EventBase_ShouldImplementIEvent()
    {
        // Act
        var @event = new TestEvent();

        // Assert
        @event.Should().BeAssignableTo<IEvent>();
    }

    [Fact]
    public void EventBase_PropertiesShouldBeInitOnly()
    {
        // Arrange & Act
        var eventId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow.AddHours(-1);
        

        // Trying to explicitly set via init would work at construction time only
        var @event2 = new TestEvent
        {
            EventId = eventId,
            OccurredAt = occurredAt
        };

        // Assert
        @event2.EventId.Should().Be(eventId);
        @event2.OccurredAt.Should().Be(occurredAt);
    }

    [Fact]
    public void EventBase_ShouldSupportDerivedEvents()
    {
        // Arrange
        var message = "Test message";

        // Act
        var @event = new TestEvent { Message = message };

        // Assert
        @event.Message.Should().Be(message);
        @event.EventId.Should().NotBeEmpty();
        @event.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }
}
