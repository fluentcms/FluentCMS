using System.Reflection;
using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Plugins;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Mcp.Admin.Tests;

public sealed class McpClientsPageTests : McpTestContext
{
    [Theory]
    [InlineData(typeof(McpClientsPage), "mcp.manage", true)]
    [InlineData(typeof(McpInstructionsPage), "mcp.manage", false)]
    [InlineData(typeof(McpPromptsPage), "mcp.manage", false)]
    [InlineData(typeof(McpActivityPage), "activity.read", false)]
    public void TheClientsAreForTheInstanceAndTheOtherPagesForTheSite(Type page, string permission, bool instanceOnly)
    {
        var declared = page.GetCustomAttribute<AdminPageAttribute>()!;

        (declared.Permission, declared.InstanceOnly).ShouldBe((permission, instanceOnly));
    }

    [Fact]
    public void TheClientsAreListedWithTheirStatusRoleAndActionsAndOnlyTheRolesOfEverySiteAreOffered()
    {
        Server.AddClient("Desk agent", "pending");
        Server.AddClient("Build bot", "approved", Editor);
        Server.AddClient("Old agent", "rejected");
        var cut = Render<McpClientsPage>();

        cut.WaitForAssertion(() => cut.FindAll(".mcp-clients tbody tr").Count.ShouldBe(3));
        var rows = cut.FindAll(".mcp-clients tbody tr");
        rows[0].TextContent.ShouldContain("Waits for approval");
        rows[0].QuerySelectorAll("fluent-button").Select(button => button.TextContent.Trim()).ShouldBe(["Approve the client Desk agent", "Reject the client Desk agent"]);
        rows[1].TextContent.ShouldContain("Editor");
        rows[1].QuerySelectorAll("fluent-button").Select(button => button.TextContent.Trim()).ShouldBe(["Change the role of the client Build bot", "Revoke the client Build bot"]);
        rows[2].QuerySelectorAll("fluent-button").Select(button => button.TextContent.Trim()).ShouldBe(["Remove the client Old agent"]);
        Field(cut, "Role").QuerySelectorAll("option").Select(option => option.TextContent).ShouldBe(["Choose a role", "Editor", "Reader"]);
        cut.Markup.ShouldContain("Service accounts come from configuration");
        Server.Requests.ShouldAllBe(request => request.NoLocale);
    }

    [Fact]
    public void AnAdministratorApprovesAClientThatRegisteredItselfChangesItsRoleAndRevokesIt()
    {
        var id = Server.AddClient("Desk agent", "pending");
        var cut = Render<McpClientsPage>();
        cut.WaitForAssertion(() => HasButton(cut, "Approve the client Desk agent").ShouldBeTrue());

        Button(cut, "Approve the client Desk agent").Click();
        cut.WaitForAssertion(() => HasButton(cut, "Approve").ShouldBeTrue());
        Field(cut, "Role").Change(Reader.ToString("D"));
        Button(cut, "Approve").Click();

        cut.WaitForAssertion(() => cut.Find(".mcp-list-message").TextContent.ShouldBe("The client Desk agent is approved with the role Reader."));
        Server.Requests.Single(request => request.Path == $"mcp/clients/{id:D}/approve").Body!.ToJsonString().ShouldBe($$"""{"role":"{{Reader:D}}"}""");

        cut.WaitForAssertion(() => HasButton(cut, "Change the role of the client Desk agent").ShouldBeTrue());
        Button(cut, "Change the role of the client Desk agent").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("This ends every token of the client"));
        Field(cut, "Role").Change(Editor.ToString("D"));
        Button(cut, "Change the role").Click();

        cut.WaitForAssertion(() => cut.Find(".mcp-list-message").TextContent.ShouldBe("The client Desk agent now has the role Editor. Its users sign in through it again."));
        Server.Client(id)["roleName"]!.GetValue<string>().ShouldBe("Editor");

        Button(cut, "Revoke the client Desk agent").Click();
        cut.WaitForAssertion(() => HasButton(cut, "Revoke").ShouldBeTrue());
        Button(cut, "Revoke").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No MCP client has registered yet."));
        Server.Clients.ShouldBeEmpty();
        Announcer.Messages.ShouldBe(
        [
            "The client Desk agent is approved with the role Reader.",
            "The client Desk agent now has the role Editor. Its users sign in through it again.",
            "The client Desk agent is revoked.",
        ]);
    }

    [Fact]
    public void AnApprovalNeedsARoleAndARejectAsksFirst()
    {
        var id = Server.AddClient("Desk agent", "pending");
        var cut = Render<McpClientsPage>();
        cut.WaitForAssertion(() => HasButton(cut, "Approve the client Desk agent").ShouldBeTrue());

        Button(cut, "Approve the client Desk agent").Click();
        cut.WaitForAssertion(() => HasButton(cut, "Approve").ShouldBeTrue());
        Button(cut, "Approve").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Choose a role."));
        Button(cut, "Cancel").Click();
        cut.WaitForAssertion(() => cut.Find(".mcp-list-message").TextContent.ShouldBe("Nothing was changed."));

        Button(cut, "Reject the client Desk agent").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Reject the client Desk agent? It is refused for good"));
        Button(cut, "Reject").Click();

        cut.WaitForAssertion(() => HasButton(cut, "Remove the client Desk agent").ShouldBeTrue());
        Server.Client(id)["status"]!.GetValue<string>().ShouldBe("rejected");
        Server.Requests.ShouldNotContain(request => request.Path.EndsWith("/approve", StringComparison.Ordinal));
    }

    [Fact]
    public void AClientRegisteredFromTheAdminShowsItsClientIdOnceInTheDialogOfASecret()
    {
        var cut = Render<McpClientsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No MCP client has registered yet."));

        Field(cut, "Name").Input("Build bot");
        Field(cut, "Redirect URIs").Input("https://bot.example.com/callback\n\n http://127.0.0.1:5000/cb ");
        Field(cut, "Role").Change(Editor.ToString("D"));
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.FindComponent<CmsSecretDialog>().Instance.Secret.ShouldNotBeNull());
        var client = Server.Clients.Single();
        cut.FindComponent<CmsSecretDialog>().Instance.Secret.ShouldBe(client["clientId"]!.GetValue<string>());
        Server.Requests.Single(request => request.Method == HttpMethod.Post).Body!.ToJsonString()
            .ShouldBe($$"""{"name":"Build bot","redirectUris":["https://bot.example.com/callback","http://127.0.0.1:5000/cb"],"role":"{{Editor:D}}"}""");
        cut.WaitForAssertion(() => cut.FindAll(".mcp-clients tbody tr").Count.ShouldBe(1));
        Field(cut, "Name").GetAttribute("value").ShouldBeNullOrEmpty();
        Announcer.Messages.ShouldBe(["The client Build bot is registered and approved."]);

        Button(cut, "Done").Click();
        cut.WaitForAssertion(() => cut.FindComponent<CmsSecretDialog>().Instance.Secret.ShouldBeNull());
    }

    [Fact]
    public void ARegistrationTheServerRefusesSaysWhyByItsField()
    {
        Server.Refuse = request => request.Method == HttpMethod.Post ? Problem(422, "FCMS1608") : null;
        var cut = Render<McpClientsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No MCP client has registered yet."));

        cut.Find("form").Submit();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Enter at least one redirect URI."));
        Server.Requests.ShouldNotContain(request => request.Method == HttpMethod.Post);

        Field(cut, "Redirect URIs").Input("http://example.com/callback");
        Field(cut, "Role").Change(Editor.ToString("D"));
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find(".mcp-register-message").TextContent.ShouldBe("The client was not registered."));
        Field(cut, "Redirect URIs").GetAttribute("aria-invalid").ShouldBe("true");
        cut.FindComponent<CmsSecretDialog>().Instance.Secret.ShouldBeNull();
    }

    // The server's cleanup removes a client nobody approved within 7 days, so an approval can find it gone.
    [Fact]
    public void AnActionOnAClientTheCleanupRemovedSaysItIsGoneAndTheListIsReadAgain()
    {
        Server.AddClient("Desk agent", "pending");
        Server.Refuse = request =>
        {
            if (request.Path.EndsWith("/approve", StringComparison.Ordinal))
            {
                Server.Clients.Clear();
                return Problem(404, "FCMS1603");
            }

            return null;
        };
        var cut = Render<McpClientsPage>();
        cut.WaitForAssertion(() => HasButton(cut, "Approve the client Desk agent").ShouldBeTrue());

        Button(cut, "Approve the client Desk agent").Click();
        cut.WaitForAssertion(() => HasButton(cut, "Approve").ShouldBeTrue());
        Field(cut, "Role").Change(Editor.ToString("D"));
        Button(cut, "Approve").Click();

        cut.WaitForAssertion(() => cut.Find(".mcp-list-message").TextContent.ShouldStartWith("The client Desk agent no longer exists"));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No MCP client has registered yet."));
        Server.Requests.Count(request => request.Method == HttpMethod.Get && request.Path == "mcp/clients").ShouldBe(2);
        Announcer.Messages.Single().ShouldStartWith("The client Desk agent no longer exists");
    }

    [Fact]
    public void AClientThatChangedMeanwhileIsReadAgain()
    {
        Server.AddClient("Desk agent", "pending");
        Server.Refuse = request => request.Path.EndsWith("/reject", StringComparison.Ordinal) ? Problem(409, "FCMS1604") : null;
        var cut = Render<McpClientsPage>();
        cut.WaitForAssertion(() => HasButton(cut, "Reject the client Desk agent").ShouldBeTrue());

        Button(cut, "Reject the client Desk agent").Click();
        cut.WaitForAssertion(() => HasButton(cut, "Reject").ShouldBeTrue());
        Button(cut, "Reject").Click();

        cut.WaitForAssertion(() => cut.Find(".mcp-list-message").TextContent.ShouldBe("The client Desk agent was changed meanwhile, so nothing was done. The list is read again."));
        Server.Requests.Count(request => request.Method == HttpMethod.Get && request.Path == "mcp/clients").ShouldBe(2);
    }

    [Fact]
    public void WithoutTheRolesNoClientIsGivenARoleButOneIsStillRevoked()
    {
        Server.AddClient("Desk agent", "approved", Editor);
        Server.Refuse = request => request.Path == "roles" ? Problem(403, "FCMS3000") : null;
        var cut = Render<McpClientsPage>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The roles could not be read"));
        Button(cut, "Change the role of the client Desk agent").HasAttribute("disabled").ShouldBeTrue();
        Button(cut, "Revoke the client Desk agent").HasAttribute("disabled").ShouldBeFalse();
    }
}
