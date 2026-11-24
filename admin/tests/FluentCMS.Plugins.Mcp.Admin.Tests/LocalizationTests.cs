using System.Text.Json.Nodes;
using Bunit;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Mcp.Admin.Tests;

// The sweep of spec 7: every page of the half under both pseudo-cultures, with a row of each list, its data marked.
public sealed class LocalizationTests : McpTestContext
{
    public static TheoryData<string, string> Pages() => PseudoSweep.Pages(typeof(McpClientsPage).Assembly);

    [Theory]
    [MemberData(nameof(Pages))]
    public void EveryTextOfEveryPageComesFromTheResources(string culture, string page)
    {
        this.UsePseudoCulture(culture);
        string Data(string text) => PseudoCulture.Transform(text, culture);
        Schema.Label = Data;
        Server.RoleLabel = Data;
        var client = Server.Client(Server.AddClient(Data("Desk agent"), "pending"));
        client["clientId"] = Data("mcp_desk");
        client["redirectUris"] = new JsonArray(Data("http://127.0.0.1:33418/callback"));
        Server.AddActivity(1);
        foreach (var member in new[] { "actorName", "clientId", "action", "targetType", "targetLabel", "locale" })
        {
            Server.Activity[0][member] = Data(Server.Activity[0][member]!.GetValue<string>());
        }

        Server.AddPrompt(Data("summarize"), Data("Summarize the entry."), Data("text"));
        Server.AddInstruction(null, Data("Write plainly."));

        IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut = page switch
        {
            nameof(McpActivityPage) => Render<McpActivityPage>(),
            nameof(McpClientsPage) => Render<McpClientsPage>(),
            nameof(McpPromptsPage) => Render<McpPromptsPage>(),
            nameof(McpInstructionsPage) => Render<McpInstructionsPage>(),
            _ => throw PseudoSweep.NoSweep(page),
        };
        var shown = page switch
        {
            nameof(McpActivityPage) => ".mcp-activity tbody tr",
            nameof(McpClientsPage) => ".mcp-clients tbody tr",
            nameof(McpPromptsPage) => ".mcp-prompts tbody tr",
            _ => "textarea",
        };

        cut.WaitForAssertion(() => cut.FindAll(shown).ShouldNotBeEmpty());
        cut.ShouldBeLocalized();
    }
}
