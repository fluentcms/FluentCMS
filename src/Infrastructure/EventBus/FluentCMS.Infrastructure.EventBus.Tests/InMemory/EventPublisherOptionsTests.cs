namespace FluentCMS.Infrastructure.EventBus.Tests.InMemory;

public class EventPublisherOptionsTests
{
    [Fact]
    public void EventPublisherOptions_DefaultMode_ShouldBeAggregate()
    {
        // Arrange & Act
        var options = new EventPublisherOptions();

        // Assert
        options.Mode.Should().Be(EventPublisherOptions.ErrorHandlingMode.Aggregate);
    }

    [Fact]
    public void EventPublisherOptions_ModeSetter_ShouldAllowSettingToFailFast()
    {
        // Arrange
        var options = new EventPublisherOptions();

        // Act
        options.Mode = EventPublisherOptions.ErrorHandlingMode.FailFast;

        // Assert
        options.Mode.Should().Be(EventPublisherOptions.ErrorHandlingMode.FailFast);
    }

    [Fact]
    public void EventPublisherOptions_ModeSetter_ShouldAllowSettingToAggregate()
    {
        // Arrange
        var options = new EventPublisherOptions 
        { 
            Mode = EventPublisherOptions.ErrorHandlingMode.FailFast 
        };

        // Act
        options.Mode = EventPublisherOptions.ErrorHandlingMode.Aggregate;

        // Assert
        options.Mode.Should().Be(EventPublisherOptions.ErrorHandlingMode.Aggregate);
    }

    [Fact]
    public void ErrorHandlingMode_ShouldHaveTwoValues()
    {
        // Arrange & Act
        var enumValues = Enum.GetValues<EventPublisherOptions.ErrorHandlingMode>();

        // Assert
        enumValues.Should().HaveCount(2);
        enumValues.Should().Contain(EventPublisherOptions.ErrorHandlingMode.FailFast);
        enumValues.Should().Contain(EventPublisherOptions.ErrorHandlingMode.Aggregate);
    }

    [Fact]
    public void ErrorHandlingMode_FailFast_ShouldHaveValue0()
    {
        // Arrange & Act
        var value = (int)EventPublisherOptions.ErrorHandlingMode.FailFast;

        // Assert
        value.Should().Be(0);
    }

    [Fact]
    public void ErrorHandlingMode_Aggregate_ShouldHaveValue1()
    {
        // Arrange & Act
        var value = (int)EventPublisherOptions.ErrorHandlingMode.Aggregate;

        // Assert
        value.Should().Be(1);
    }
}
