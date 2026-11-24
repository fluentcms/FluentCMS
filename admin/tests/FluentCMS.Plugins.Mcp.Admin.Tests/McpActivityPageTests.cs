using Bunit;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Mcp.Admin.Tests;

public sealed class McpActivityPageTests : McpTestContext
{
    [Fact]
    public void TheAgentWritesOfTheSiteAreListedInPages()
    {
        Server.AddActivity(60);
        var cut = Render<McpActivityPage>();

        cut.WaitForAssertion(() => cut.FindAll(".mcp-activity tbody tr").Count.ShouldBe(50));
        Server.Requests.Single().Path.ShouldBe("activity?origin=mcp&page=1&pageSize=50");
        cut.Find(".mcp-activity tbody tr").TextContent.ShouldContain("Entry 1");
        cut.Markup.ShouldContain("Page 1 of 2");
        Button(cut, "Previous page").HasAttribute("disabled").ShouldBeTrue();

        Button(cut, "Next page").Click();

        cut.WaitForAssertion(() => cut.FindAll(".mcp-activity tbody tr").Count.ShouldBe(10));
        Server.Requests[^1].Path.ShouldBe("activity?origin=mcp&page=2&pageSize=50");
        cut.Markup.ShouldContain("Page 2 of 2");
        Button(cut, "Next page").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void ASiteWhereNoAgentWroteSaysSo()
    {
        var cut = Render<McpActivityPage>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No agent has written anything in this site."));
    }
}
