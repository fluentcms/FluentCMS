using System.Text.Json.Nodes;
using Bunit;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.AiAssist.Admin.Tests;

// The sweep of spec 7: every page of the half under both pseudo-cultures, with a call of each kind of principal, its data
// marked; the moment of a call is data in the user's formats.
public sealed class LocalizationTests : AiTestContext
{
    public static TheoryData<string, string> Pages() => PseudoSweep.Pages(typeof(AiUsagePage).Assembly);

    [Theory]
    [MemberData(nameof(Pages))]
    public void EveryTextOfEveryPageComesFromTheResources(string culture, string page)
    {
        this.UsePseudoCulture(culture);
        string Data(string text) => PseudoCulture.Transform(text, culture);
        Server.Usage = FakeAiServer.UsageOf([Call("adminUser", Data("Ada"), "rewrite", Data("article"), Data("title")), Call("apiKey", "", "altText", "", "")]);

        IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut = page switch
        {
            nameof(AiUsagePage) => Render<AiUsagePage>(),
            _ => throw PseudoSweep.NoSweep(page),
        };

        cut.WaitForAssertion(() => cut.FindAll(".ai-usage-table tbody tr").Count.ShouldBe(4));
        cut.ShouldBeLocalized();

        JsonObject Call(string kind, string name, string action, string type, string field) => new()
        {
            ["id"] = Guid.CreateVersion7(),
            ["principalKind"] = kind,
            ["principalId"] = Guid.CreateVersion7(),
            ["principalName"] = name,
            ["action"] = action,
            ["type"] = type,
            ["field"] = field,
            ["provider"] = Data("openAi"),
            ["model"] = Data("m"),
            ["promptTokens"] = 30L,
            ["completionTokens"] = 10L,
            ["durationMs"] = 1200,
            ["succeeded"] = true,
            ["createdAt"] = "2026-11-20T09:30:00Z",
        };
    }
}
