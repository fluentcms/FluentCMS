using Bunit;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Mcp.Admin.Tests;

public sealed class McpPromptsPageTests : McpTestContext
{
    [Fact]
    public void APromptIsStoredWithAnArgumentAndChanged()
    {
        var cut = Render<McpPromptsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The site has no stored prompt yet."));

        Field(cut, "Name").Input("summarize");
        Field(cut, "Title").Input("Summarize an entry");
        Button(cut, "Add an argument").Click();
        cut.WaitForAssertion(() => cut.FindAll("label").Any(label => label.TextContent.Trim().TrimEnd('*').TrimEnd() == "Name of argument 1").ShouldBeTrue());
        Field(cut, "Name of argument 1").Input("id");
        Field(cut, "Template").Input("Summarize the entry {{id}}.");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.FindAll(".mcp-prompts tbody tr").Count.ShouldBe(1));
        Server.Requests.Single(request => request.Method == HttpMethod.Post).Body!.ToJsonString().ShouldBe(
            """{"name":"summarize","title":"Summarize an entry","description":null,"arguments":[{"name":"id","description":null,"required":false}],"template":"Summarize the entry {{id}}."}""");
        cut.Find(".mcp-list-message").TextContent.ShouldBe("The prompt summarize is created.");
        Field(cut, "Name").GetAttribute("value").ShouldBeNullOrEmpty();

        Button(cut, "Change the prompt summarize").Click();
        cut.WaitForAssertion(() => cut.Find(".cms-card-title").TextContent.ShouldBe("Change the prompt summarize"));
        Field(cut, "Name").GetAttribute("value").ShouldBe("summarize");
        HasButton(cut, "Save the prompt").ShouldBeTrue();
        Field(cut, "Name of argument 1").GetAttribute("value").ShouldBe("id");
        Field(cut, "Template").Input("Summarize {{id}} in one line.");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find(".mcp-list-message").TextContent.ShouldBe("The prompt summarize is saved."));
        var put = Server.Requests.Single(request => request.Method == HttpMethod.Put);
        put.Path.ShouldBe($"mcp/prompts/{Server.Prompts.Single()["id"]!.GetValue<Guid>():D}");
        Server.Prompts.Single()["template"]!.GetValue<string>().ShouldBe("Summarize {{id}} in one line.");
        Announcer.Messages.ShouldBe(["The prompt summarize is created.", "The prompt summarize is saved."]);
    }

    [Fact]
    public void TheRefusalsOfThePromptArePutOnTheirFields()
    {
        Server.AddPrompt("summarize", "Summarize.");
        Server.Refuse = request => request.Method == HttpMethod.Post && request.Body!["template"]!.GetValue<string>().Contains("{{", StringComparison.Ordinal)
            ? new FluentCMS.Admin.Api.ApiProblemException(new FluentCMS.Admin.Api.ApiProblem
            {
                Status = 422,
                Code = "FCMS1001",
                Errors =
                [
                    new FluentCMS.Admin.Api.ApiProblemError { Path = "arguments[0].name", Code = "FCMS1623" },
                    new FluentCMS.Admin.Api.ApiProblemError { Path = "template", Code = "FCMS1627" },
                ],
            })
            : null;
        var cut = Render<McpPromptsPage>();
        cut.WaitForAssertion(() => cut.FindAll(".mcp-prompts tbody tr").Count.ShouldBe(1));

        Field(cut, "Name").Input("summarize");
        Field(cut, "Template").Input("Again.");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Field(cut, "Name").GetAttribute("aria-invalid").ShouldBe("true"));
        cut.Markup.ShouldContain("Another prompt of the site has this name.");

        Field(cut, "Name").Input("translate");
        Button(cut, "Add an argument").Click();
        cut.WaitForAssertion(() => cut.FindAll("label").Any(label => label.TextContent.Trim().TrimEnd('*').TrimEnd() == "Name of argument 1").ShouldBeTrue());
        Field(cut, "Name of argument 1").Input("9 bad");
        Field(cut, "Template").Input("Translate {{text}}.");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Field(cut, "Template").GetAttribute("aria-invalid").ShouldBe("true"));
        Field(cut, "Name of argument 1").GetAttribute("aria-invalid").ShouldBe("true");
        Field(cut, "Name").GetAttribute("aria-invalid").ShouldNotBe("true");
        cut.Markup.ShouldContain("The template names an argument that the prompt does not declare.");
        cut.Find(".mcp-form-message").TextContent.ShouldBe("The prompt was not saved.");
    }

    [Fact]
    public void APromptIsDeletedAfterAQuestionAndOneDeletedMeanwhileIsReadAgain()
    {
        var kept = Server.AddPrompt("summarize", "Summarize.");
        Server.AddPrompt("translate", "Translate.");
        Server.Refuse = request => request.Method == HttpMethod.Delete && request.Path.EndsWith(kept.ToString("D"), StringComparison.Ordinal) ? Problem(404, "FCMS1624") : null;
        var cut = Render<McpPromptsPage>();
        cut.WaitForAssertion(() => cut.FindAll(".mcp-prompts tbody tr").Count.ShouldBe(2));

        Button(cut, "Delete the prompt translate").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Delete the prompt translate? Agents are no longer offered it."));
        Button(cut, "Delete the prompt").Click();
        cut.WaitForAssertion(() => cut.FindAll(".mcp-prompts tbody tr").Count.ShouldBe(1));

        Button(cut, "Delete the prompt summarize").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Delete the prompt summarize?"));
        Button(cut, "Delete the prompt").Click();
        cut.WaitForAssertion(() => cut.Find(".mcp-list-message").TextContent.ShouldBe("The prompt summarize no longer exists. The list is read again."));
        Announcer.Messages.ShouldBe(["The prompt translate is deleted.", "The prompt summarize no longer exists. The list is read again."]);
    }
}
