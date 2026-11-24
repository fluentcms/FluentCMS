using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Plugins.Mcp.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The MCP half's calls (spec 5.4) against the server half: its records held to the OpenAPI document, a client that registered
// itself approved, given another role and revoked, one registered by the administrator, the instructions and a prompt of the
// site written and changed, the agent writes read, and a caller who holds mcp.manage in a site only, whom the clients refuse.
public sealed class McpTests
{
    private const string Prefix = "/api/management/v1/";

    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task TheRecordsOfTheHalfMatchTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        var paths = document["paths"]!;
        var records = McpJsonContext.Default;
        List<string> gaps = [];

        void Check(System.Text.Json.Serialization.Metadata.JsonTypeInfo record, string path, string method, string status = "200") =>
            OpenApiContractTests.Check(record, OpenApiContractTests.Answer(paths, Prefix + path, method, status), $"{path} {method}", document, gaps);

        Check(records.ItemListMcpClient, "mcp/clients", "get");
        Check(records.McpClient, "mcp/clients", "post", "201");
        Check(records.McpClient, "mcp/clients/{id}/approve", "post");
        Check(records.McpClient, "mcp/clients/{id}/role", "put");
        Check(records.McpClient, "mcp/clients/{id}/reject", "post");
        Check(records.ItemListMcpRole, "roles", "get");
        Check(records.ItemListMcpInstruction, "mcp/instructions", "get");
        Check(records.McpInstruction, "mcp/instructions", "put");
        Check(records.McpInstruction, "mcp/instructions/{type}", "put");
        Check(records.ItemListMcpPrompt, "mcp/prompts", "get");
        Check(records.McpPrompt, "mcp/prompts", "post", "201");
        Check(records.McpPrompt, "mcp/prompts/{id}", "put");
        Check(records.McpActivityAnswer, "activity", "get");

        gaps.ShouldBeEmpty();
        foreach (var path in (string[])["mcp/clients/{id}", "mcp/instructions", "mcp/instructions/{type}", "mcp/prompts/{id}"])
        {
            paths[Prefix + path]?["delete"]?["responses"]?["204"].ShouldNotBeNull();
        }

        Body(document, "mcp/clients", "post").Select(member => member.Key).ShouldBe(["name", "redirectUris", "role"], ignoreOrder: true);
        Body(document, "mcp/clients/{id}/approve", "post").Select(member => member.Key).ShouldBe(["role"]);
        Body(document, "mcp/clients/{id}/role", "put").Select(member => member.Key).ShouldBe(["role"]);
        Body(document, "mcp/instructions", "put").Select(member => member.Key).ShouldBe(["text"]);
        Body(document, "mcp/prompts", "post").Select(member => member.Key).ShouldBe(["name", "title", "description", "arguments", "template"], ignoreOrder: true);
        var activity = paths[Prefix + "activity"]!["get"]!["parameters"]!.AsArray()
            .Select(parameter => parameter!["$ref"] is { } reference ? document["components"]!["parameters"]![reference.GetValue<string>().Split('/')[^1]]! : parameter!)
            .Select(parameter => parameter["name"]!.GetValue<string>())
            .ToList();
        activity.ShouldContain("origin");
        activity.ShouldContain("page");
        activity.ShouldContain("pageSize");
    }

    [Fact]
    public async Task AClientThatRegisteredItselfIsApprovedGivenAnotherRoleAndRevokedAndOneIsRegisteredByTheAdministrator()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var api = client.Api;
        var roles = await api.GetRolesAsync(Token);
        var (editor, author) = (roles.Single(role => role.Name == BuiltInRoles.EditorName), roles.Single(role => role.Name == BuiltInRoles.AuthorName));

        using (var agent = admin.Host.CreateSecureClient())
        {
            using var body = new StringContent("""{ "client_name": "Desk agent", "redirect_uris": ["http://127.0.0.1:41000/callback"], "scope": "openid mcp" }""", Encoding.UTF8, "application/json");
            using var registered = await agent.PostAsync("/connect/register", body, Token);
            registered.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var pending = (await api.GetClientsAsync(Token)).Single(item => item.Name == "Desk agent");
        (pending.Status, pending.RoleId, pending.LastActivityAt).ShouldBe(("pending", (Guid?)null, (DateTimeOffset?)null));

        var approved = await api.ApproveClientAsync(pending.Id, editor.Id.ToString("D"), Token);
        (approved.Status, approved.RoleId, approved.RoleName).ShouldBe(("approved", editor.Id, BuiltInRoles.EditorName));
        Has(await Should.ThrowAsync<ApiProblemException>(() => api.ApproveClientAsync(pending.Id, editor.Id.ToString("D"), Token)), McpProblems.ClientNotPending).ShouldBeTrue();
        (await api.ChangeClientRoleAsync(pending.Id, author.Id.ToString("D"), Token)).RoleName.ShouldBe(BuiltInRoles.AuthorName);
        Has(await Should.ThrowAsync<ApiProblemException>(() => api.ChangeClientRoleAsync(pending.Id, Guid.NewGuid().ToString("D"), Token)), McpProblems.ClientRoleUnknown).ShouldBeTrue();

        await api.RevokeClientAsync(pending.Id, Token);
        (await api.GetClientsAsync(Token)).ShouldNotContain(item => item.Id == pending.Id);
        Has(await Should.ThrowAsync<ApiProblemException>(() => api.RejectClientAsync(pending.Id, Token)), McpProblems.ClientNotFound).ShouldBeTrue();

        var made = await api.RegisterClientAsync("Build bot", ["https://bot.example.com/callback"], editor.Id.ToString("D"), Token);
        (made.Status, made.RoleName).ShouldBe(("approved", BuiltInRoles.EditorName));
        made.RedirectUris.ShouldBe(["https://bot.example.com/callback"]);
        made.ClientId.ShouldNotBeNullOrWhiteSpace();
        Has(await Should.ThrowAsync<ApiProblemException>(() => api.RegisterClientAsync(null, ["http://example.com/callback"], editor.Id.ToString("D"), Token)), McpProblems.ClientRegistrationInvalid).ShouldBeTrue();

        // What the administrator did came through the Management API, so none of it is an agent write; the client's own
        // registration came in for MCP, and may be listed once its row is written.
        var writes = await api.GetAgentWritesAsync(1, Token);
        (writes.Page, writes.PageSize).ShouldBe((1, McpApi.ActivityPageSize));
        writes.Items.Select(row => row.Action).ShouldBeSubsetOf(["mcp.client_registered"]);
    }

    [Fact]
    public async Task TheInstructionsOfTheSiteAndAStoredPromptAreWrittenChangedAndRemoved()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var api = client.Api;

        (await api.SaveInstructionAsync(null, "Write in plain English.", Token)).Type.ShouldBeNull();
        (await api.SaveInstructionAsync("article", "Keep a title short.", Token)).Text.ShouldBe("Keep a title short.");
        (await api.SaveInstructionAsync("article", "Keep a title under 60 characters.", Token)).Orphaned.ShouldBeFalse();
        (await api.GetInstructionsAsync(Token)).Select(row => (row.Type, row.Text)).ShouldBe([(null, "Write in plain English."), ("article", "Keep a title under 60 characters.")]);
        Has(await Should.ThrowAsync<ApiProblemException>(() => api.SaveInstructionAsync("no-such-type", "Text.", Token)), McpProblems.InstructionTypeNotFound).ShouldBeTrue();
        Has(await Should.ThrowAsync<ApiProblemException>(() => api.SaveInstructionAsync(null, new string('x', 4001), Token)), McpProblems.InvalidInstruction).ShouldBeTrue();
        await api.DeleteInstructionAsync("article", Token);
        await api.DeleteInstructionAsync(null, Token);
        (await api.GetInstructionsAsync(Token)).ShouldBeEmpty();
        Has(await Should.ThrowAsync<ApiProblemException>(() => api.DeleteInstructionAsync(null, Token)), McpProblems.InstructionNotFound).ShouldBeTrue();

        var draft = new McpPromptDraft { Name = "summarize", Title = "Summarize an entry", Template = "Summarize the entry {{id}}." };
        draft.Arguments.Add(new McpArgumentDraft { Name = "id", Required = true });
        var created = await api.SavePromptAsync(null, draft, Token);
        (created.Name, created.Arguments.Single().Name, created.Arguments.Single().Required).ShouldBe(("summarize", "id", (bool?)true));
        draft.Template = "Summarize {{id}} in one line.";
        (await api.SavePromptAsync(created.Id, draft, Token)).Template.ShouldBe("Summarize {{id}} in one line.");
        Has(await Should.ThrowAsync<ApiProblemException>(() => api.SavePromptAsync(null, draft, Token)), McpProblems.PromptNameTaken).ShouldBeTrue();

        draft.Name = "other";
        draft.Template = "Uses {{missing}}.";
        var refused = await Should.ThrowAsync<ApiProblemException>(() => api.SavePromptAsync(null, draft, Token));
        McpProblems.Errors(refused).ShouldContain(("template", McpProblems.UndeclaredPromptArgument));

        (await api.GetPromptsAsync(Token)).Select(prompt => prompt.Name).ShouldBe(["summarize"]);
        await api.DeletePromptAsync(created.Id, Token);
        Has(await Should.ThrowAsync<ApiProblemException>(() => api.DeletePromptAsync(created.Id, Token)), McpProblems.PromptNotFound).ShouldBeTrue();
    }

    // The clients page is instance-only: a role of a site with mcp.manage writes the site's instructions and is refused the clients.
    [Fact]
    public async Task ACallerWhoHoldsMcpManageInASiteOnlyWritesItsInstructionsAndIsRefusedTheClients()
    {
        await using var admin = new AdminHost();
        using var super = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var role = new JsonObject
        {
            ["name"] = "Agent steward",
            ["siteId"] = DefaultSite.Id,
            ["grants"] = new JsonArray(new JsonObject { ["permission"] = "mcp.manage" }, new JsonObject { ["permission"] = CorePermissions.SchemaRead }),
        };
        await super.Api.SendAsync(new ApiRequest(HttpMethod.Post, "roles") { Body = role }, Token);
        using var client = await admin.SignInAsync("Agent steward");

        (await client.Api.SaveInstructionAsync(null, "Write in plain English.", Token)).Text.ShouldBe("Write in plain English.");
        (await client.Api.GetPromptsAsync(Token)).ShouldBeEmpty();
        McpProblems.IsForbidden(await Should.ThrowAsync<ApiProblemException>(() => client.Api.GetClientsAsync(Token))).ShouldBeTrue();
    }

    private static bool Has(ApiProblemException refused, string code) =>
        refused.Problem.Code == code || refused.Problem.Errors.Any(error => error.Code == code);

    private static JsonObject Body(JsonObject document, string path, string method)
    {
        var schema = document["paths"]![Prefix + path]![method]!["requestBody"]!["content"]!["application/json"]!["schema"]!;
        while (schema["oneOf"] is JsonArray alternatives)
        {
            schema = alternatives.Single(alternative => alternative?["type"]?.GetValue<string>() != "null")!;
        }

        var reference = schema["$ref"]?.GetValue<string>();
        var resolved = reference is null ? schema : document["components"]!["schemas"]![reference.Split('/')[^1]]!;
        return resolved["properties"]!.AsObject();
    }
}
