using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests;

public class SmokeTests
{
    [Fact]
    public void TestPipelineRuns()
    {
        (1 + 1).ShouldBe(2);
    }
}
