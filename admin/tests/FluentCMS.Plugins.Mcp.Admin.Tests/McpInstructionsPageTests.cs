using Bunit;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Mcp.Admin.Tests;

public sealed class McpInstructionsPageTests : McpTestContext
{
    [Fact]
    public void TheGuidelinesOfTheSiteAreWrittenChangedAndRemoved()
    {
        var cut = Render<McpInstructionsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No content type has instructions yet."));
        HasButton(cut, "Remove the guidelines").ShouldBeFalse();

        Field(cut, "Guidelines").Input("Write in plain English.");
        Button(cut, "Save the guidelines").Click();

        cut.WaitForAssertion(() => HasButton(cut, "Remove the guidelines").ShouldBeTrue());
        Server.Requests.Single(request => request.Method == HttpMethod.Put).Path.ShouldBe("mcp/instructions");
        Server.Instructions.Single()["text"]!.GetValue<string>().ShouldBe("Write in plain English.");

        Field(cut, "Guidelines").Input("Write in plain English. Never publish.");
        Button(cut, "Save the guidelines").Click();
        cut.WaitForAssertion(() => Server.Instructions.Single()["text"]!.GetValue<string>().ShouldBe("Write in plain English. Never publish."));

        Button(cut, "Remove the guidelines").Click();
        cut.WaitForAssertion(() => HasButton(cut, "Remove the guidelines").ShouldBeFalse());
        Server.Instructions.ShouldBeEmpty();
        Field(cut, "Guidelines").GetAttribute("value").ShouldBeNullOrEmpty();
        Announcer.Messages.ShouldBe(["Saved.", "Saved.", "The guidelines of the site are removed."]);
    }

    [Fact]
    public void TheInstructionsOfOneTypeAreWrittenChangedAndRemoved()
    {
        var cut = Render<McpInstructionsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Choose a content type"));
        Field(cut, "Content type").QuerySelectorAll("option").Select(option => option.TextContent).ShouldBe(["Choose a content type", "Article", "product"]);

        Field(cut, "Content type").Change("article");
        cut.WaitForAssertion(() => cut.FindAll("label").Any(label => label.TextContent.Trim() == "Article").ShouldBeTrue());
        Field(cut, "Article").Input("Keep a title under 60 characters.");
        Button(cut, "Save the instructions of Article").Click();

        cut.WaitForAssertion(() => cut.FindAll("section.mcp-instruction h3").Select(heading => heading.TextContent).ShouldBe(["Article"]));
        Server.Requests.Single(request => request.Method == HttpMethod.Put).Path.ShouldBe("mcp/instructions/article");
        Field(cut, "Content type").QuerySelectorAll("option").Select(option => option.TextContent).ShouldBe(["Choose a content type", "product"]);

        Field(cut, "Article").Input("Keep a title under 50 characters.");
        Button(cut, "Save the instructions of Article").Click();
        cut.WaitForAssertion(() => Server.Instructions.Single()["text"]!.GetValue<string>().ShouldBe("Keep a title under 50 characters."));

        Button(cut, "Remove the instructions of Article").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No content type has instructions yet."));
        Server.Requests.ShouldContain(request => request.Method == HttpMethod.Delete && request.Path == "mcp/instructions/article");
        Announcer.Messages[^1].ShouldBe("The instructions of Article are removed.");
    }

    [Fact]
    public void TheInstructionsOfATypeNoLongerInTheSchemaSaySoAndATextTheServerRefusesIsMarked()
    {
        Server.AddInstruction("gone", "Old text.", orphaned: true);
        Server.Refuse = request => request.Method == HttpMethod.Put ? Problem(422, "FCMS1620", "text") : null;
        var cut = Render<McpInstructionsPage>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("This type is no longer in the schema"));
        Field(cut, "gone").Input("New text.");
        Button(cut, "Save the instructions of gone").Click();

        cut.WaitForAssertion(() => Field(cut, "gone").GetAttribute("aria-invalid").ShouldBe("true"));
        cut.Markup.ShouldContain("A text is up to 4,000 characters");
    }

    [Fact]
    public void ACallerWhoHoldsMcpManageInTheSiteOnlyIsNotLinkedToTheClients()
    {
        Admin.SiteOnly.Add("mcp.manage");
        var cut = Render<McpInstructionsPage>();

        cut.WaitForAssertion(() => cut.FindAll("nav a").Select(link => link.GetAttribute("href")).ShouldBe(["mcp/prompts", "mcp/activity"]));
    }

    [Fact]
    public void ACallerWhoMayNotReadTheActivityIsNotLinkedToTheAgentWrites()
    {
        Admin.Denied.Add("activity.read");
        var cut = Render<McpPromptsPage>();

        cut.WaitForAssertion(() => cut.FindAll("nav a").Select(link => link.GetAttribute("href")).ShouldBe(["mcp/clients", "mcp/instructions"]));
    }
}
