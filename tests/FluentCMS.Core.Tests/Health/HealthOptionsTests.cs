using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Health;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Health;

public class HealthOptionsTests
{
    [Fact]
    public void TheHeartbeatLimitDefaultsToTwoMinutes() =>
        new HealthOptions().JobRunnerHeartbeatTimeout.ShouldBe(TimeSpan.FromMinutes(2));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void AHeartbeatLimitThatIsNotPositiveIsRefused(int seconds)
    {
        var options = new HealthOptions { JobRunnerHeartbeatTimeout = TimeSpan.FromSeconds(seconds) };

        var exception = Should.Throw<FluentCmsConfigurationException>(options.Validate);

        exception.Code.ShouldBe("FCMS1101");
        exception.Detail.ShouldStartWith(nameof(HealthOptions.JobRunnerHeartbeatTimeout));
    }

    [Fact]
    public void NoLimitCanBeAskedForWithAnInfiniteTimeout() =>
        new HealthOptions { JobRunnerHeartbeatTimeout = Timeout.InfiniteTimeSpan }.Validate();
}
