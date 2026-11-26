using System.Globalization;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.AiAssist.Admin.Tests;

// The usage page (spec 5.5): the calls of the site, newest first, each with the name its principal had, the totals of the
// filter by principal and action, and the filters and pages it reads them by.
public sealed class AiUsagePageTests : AiTestContext
{
    private static readonly Guid Ada = Guid.Parse("0193a1b2-0000-7000-8000-0000000000a1");
    private static readonly Guid Key = Guid.Parse("0193a1b2-0000-7000-8000-0000000000b2");

    [Fact]
    public void TheUsageIsListedWithTheNamesOfItsPrincipalsAndTheTotalsOfTheFilter()
    {
        Server.Usage = FakeAiServer.UsageOf([Row(Ada, "Ada", "rewrite", 30, 10), Row(Key, "", "altText", 100, 20, type: "", field: "", kind: "apiKey", succeeded: false)]);

        var page = Render<AiUsagePage>();

        page.WaitForAssertion(() => page.Find(".ai-usage-totals").TextContent.ShouldBe("2 calls, 130 prompt tokens and 30 completion tokens."));
        page.Find("h1").TextContent.ShouldBe("AI usage");
        var calls = page.FindAll(".ai-usage-table")[1].QuerySelectorAll("tbody tr").Select(row => row.QuerySelectorAll("th, td").Select(cell => cell.TextContent).ToArray()).ToList();
        // Written by the runtime as the page writes it: for en the space before AM is a narrow no-break one on Linux.
        var at = new DateTime(2026, 11, 20, 9, 30, 0, DateTimeKind.Utc).ToString("g", CultureInfo.CurrentCulture);
        calls[0].ShouldBe([$"{at} UTC", "Ada", "Rewrite", "article.title", "m", "30", "10", "1,200 ms", "Succeeded"]);
        calls[1][1].ShouldBe($"API key {Key:D}");
        calls[1][2].ShouldBe("Suggest alt text of an image");
        calls[1][3].ShouldBe("An asset of the media library");
        calls[1][8].ShouldBe("Failed");
        page.FindAll(".ai-usage-table")[0].QuerySelectorAll("tbody th").Select(cell => cell.TextContent).ShouldBe(["Ada", $"API key {Key:D}"]);
        page.FindAll(".ai-usage-truncated").ShouldBeEmpty();
        Server.Requests.ShouldHaveSingleItem().Path.ShouldBe("ai/usage?page=1&pageSize=50");
    }

    // The server answers at most 100 groups, those that used the most tokens, and says when the filter matched more.
    [Fact]
    public void GroupsCutByTheServerAreNotedUnderTheirTableWithANarrowerFilterSuggested()
    {
        Server.Usage = FakeAiServer.UsageOf([Row(Ada, "Ada", "rewrite", 30, 10), Row(Key, "", "altText", 100, 20, kind: "apiKey")], groupsTruncated: true);

        var page = Render<AiUsagePage>();

        page.WaitForAssertion(() => page.Find(".ai-usage-truncated").TextContent.ShouldBe(
            "The table shows the 2 principals and actions that used the most tokens, and the filter matches more. Narrow it by principal, action or days to see them all."));
        var note = page.Find(".ai-usage-truncated");
        note.GetAttribute("role").ShouldBe("status");
        note.PreviousElementSibling!.QuerySelector("caption")!.TextContent.ShouldBe("By principal and action");

        Server.Usage = FakeAiServer.UsageOf([Row(Ada, "Ada", "rewrite", 30, 10)]);
        page.FindAll("select")[0].Change(Ada.ToString("D"));

        page.WaitForAssertion(() => page.FindAll(".ai-usage-truncated").ShouldBeEmpty());
    }

    [Fact]
    public void TheFiltersAndThePagesAreSentAsTheServerReadsThem()
    {
        Server.Usage = FakeAiServer.UsageOf([Row(Ada, "Ada", "rewrite", 30, 10)], total: 120);
        var page = Render<AiUsagePage>();
        page.WaitForAssertion(() => page.Markup.ShouldContain("Page 1 of 3"));

        page.FindAll("select")[0].Change(Ada.ToString("D"));
        page.WaitForAssertion(() => Server.Requests.Count.ShouldBe(2));
        page.FindAll("select")[1].Change("translate");
        page.WaitForAssertion(() => Server.Requests.Count.ShouldBe(3));
        page.FindAll("input")[0].Change("11/01/2026");
        page.WaitForAssertion(() => Server.Requests.Count.ShouldBe(4));
        page.FindAll("input")[1].Change("11/19/2026");
        page.WaitForAssertion(() => Server.Requests.Count.ShouldBe(5));
        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Next page").Click();
        page.WaitForAssertion(() => Server.Requests.Count.ShouldBe(6));

        Server.Requests[4].Path.ShouldBe($"ai/usage?principalId={Ada:D}&action=translate&from=2026-11-01T00%3A00%3A00Z&to=2026-11-20T00%3A00%3A00Z&page=1&pageSize=50");
        Server.Requests[5].Path.ShouldEndWith("&page=2&pageSize=50");
        page.FindAll("select")[0].QuerySelectorAll("option").Select(option => option.TextContent).ShouldContain("Ada");
    }

    [Fact]
    public void ARefusedReadShowsItsReasonAndCanBeTriedAgain()
    {
        Server.UsageRefusal = new ApiProblem { Status = 403, Code = "FCMS2001", Title = "Forbidden" };
        var page = Render<AiUsagePage>();
        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.ShouldBe("Forbidden"));

        Server.UsageRefusal = null;
        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Try again").Click();

        page.WaitForAssertion(() => page.Find("p").TextContent.ShouldBe("No AI call matches."));
    }

    private static JsonObject Row(Guid principal, string name, string action, long prompt, long completion, string type = "article", string field = "title", string kind = "adminUser", bool succeeded = true) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["principalKind"] = kind,
        ["principalId"] = principal,
        ["principalName"] = name,
        ["action"] = action,
        ["type"] = type,
        ["field"] = field,
        ["provider"] = "openAi",
        ["model"] = "m",
        ["promptTokens"] = prompt,
        ["completionTokens"] = completion,
        ["durationMs"] = 1200,
        ["succeeded"] = succeeded,
        ["createdAt"] = "2026-11-20T09:30:00Z",
    };
}
