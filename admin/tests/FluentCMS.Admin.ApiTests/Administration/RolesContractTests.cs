using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Administration.Common;
using FluentCMS.Admin.Administration.Roles;
using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests.Administration;

// The records of the roles pages held to the reference host's OpenAPI document, and the save of the editor against the
// real server: a save replaces every grant, so the constraints the editor reads and does not edit come back unchanged.
public sealed class RolesContractTests
{
    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task TheRecordsOfTheRolesPagesMatchTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        var paths = document["paths"]!;
        List<string> gaps = [];

        OpenApiContractTests.Check(RolesJsonContext.Default.RoleRecord, OpenApiContractTests.Answer(paths, "/api/management/v1/roles/{id}", "get"), "roles/{id}", document, gaps);
        OpenApiContractTests.Check(RolesJsonContext.Default.RoleRecord, OpenApiContractTests.Answer(paths, "/api/management/v1/roles", "post", "201"), "roles post", document, gaps);
        OpenApiContractTests.Check(RolesJsonContext.Default.RoleRecord, OpenApiContractTests.Answer(paths, "/api/management/v1/roles/{id}", "put"), "roles/{id} put", document, gaps);
        OpenApiContractTests.Check(RolesJsonContext.Default.ItemListPermissionRecord, OpenApiContractTests.Answer(paths, "/api/management/v1/permissions", "get"), "permissions", document, gaps);
        OpenApiContractTests.Check(RolesJsonContext.Default.ItemListRowRuleRecord, OpenApiContractTests.Answer(paths, "/api/management/v1/permissions/row-rules", "get"), "permissions/row-rules", document, gaps);
        OpenApiContractTests.Check(RolesJsonContext.Default.ItemListFolderRecord, OpenApiContractTests.Answer(paths, "/api/management/v1/media/folders", "get"), "media/folders", document, gaps);
        gaps.ShouldBeEmpty();

        // What the editor sends, and the sources it groups the catalogue by.
        Properties(Body(paths, "/api/management/v1/roles", "post"), document).ShouldBe(["name", "description", "siteId", "requireTwoFactor", "grants"], ignoreOrder: true);
        Properties(Body(paths, "/api/management/v1/roles/{id}", "put"), document).ShouldBe(["name", "description", "requireTwoFactor", "grants"], ignoreOrder: true);
        paths["/api/management/v1/roles/{id}"]!["delete"]!["responses"]!["204"].ShouldNotBeNull();
        var permission = Resolve(OpenApiContractTests.Answer(paths, "/api/management/v1/permissions", "get"), document)!["properties"]!["items"]!["items"];
        Resolve(Resolve(permission, document)!["properties"]!["source"], document)!["enum"]!.AsArray().Select(value => value!.GetValue<string>())
            .ShouldBe([GrantCatalogue.Core, GrantCatalogue.Content, GrantCatalogue.Plugin, GrantCatalogue.Settings], ignoreOrder: true);

        // Every member of a grant's constraints, which the editor sends back as it read them whatever it shows.
        var grant = Resolve(OpenApiContractTests.Answer(paths, "/api/management/v1/roles/{id}", "get"), document)!["properties"]!["grants"]!["items"];
        Properties(Resolve(grant, document)!["properties"]!["constraints"]!, document).ShouldBe(["sites", "locales", "fields", "rowRule", "resources"], ignoreOrder: true);
    }

    [Fact]
    public async Task ARoleWithConstrainedGrantsIsSavedWithItsNameChangedAndKeepsEveryConstraint()
    {
        await using var admin = new AdminHost();
        using var super = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var made = await super.Api.CreateRoleAsync(new JsonObject
        {
            ["name"] = "Reviewers",
            ["siteId"] = DefaultSite.Id,
            ["grants"] = new JsonArray(
                new JsonObject { ["permission"] = "schema.read" },
                new JsonObject
                {
                    ["permission"] = "content.article.read",
                    ["constraints"] = new JsonObject
                    {
                        ["sites"] = new JsonArray(DefaultSite.Id.ToString("D")),
                        ["locales"] = new JsonArray("en"),
                        ["fields"] = new JsonArray("title", "body"),
                        ["rowRule"] = BuiltInRoles.OwnEntriesRule,
                        ["resources"] = new JsonArray("folder-1", "folder-2"),
                    },
                },
                new JsonObject { ["permission"] = "media.read", ["constraints"] = new JsonObject { ["resources"] = new JsonArray("0193a1b2-0000-7000-8000-0000000000f1") } }),
        }, Token);
        var read = await super.Api.GetRoleAsync(made.Id, Token);
        read.Grants.Count(grant => grant.Constraints is not null).ShouldBe(2);

        var draft = RoleDraft.From(read);
        draft.Name = "Article reviewers";
        var saved = await super.Api.UpdateRoleAsync(read.Id, draft.ToJson(), Token);

        saved.Name.ShouldBe("Article reviewers");
        var again = await super.Api.GetRoleAsync(read.Id, Token);
        // The server answers the grants in an order of its own.
        again.Grants.Select(grant => grant.Permission).ShouldBe(read.Grants.Select(grant => grant.Permission), ignoreOrder: true);
        foreach (var before in read.Grants)
        {
            JsonNode.DeepEquals(before.Constraints, again.Grants.Single(grant => grant.Permission == before.Permission).Constraints).ShouldBeTrue(before.Permission);
        }
    }

    // The editor's identity of a grant is the server's: what it sends is the grant it reads back once the server has
    // normalized it, the rules it offers are the registered ones, and a rule that is not registered is refused on its path.
    [Fact]
    public async Task AGrantReadBackHasTheIdentityItWasSentWithAndAnUnregisteredRowRuleIsRefusedOnItsField()
    {
        await using var admin = new AdminHost();
        using var super = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var sent = new GrantDraft("content.article.read", new JsonObject
        {
            ["locales"] = new JsonArray("FR", "en", "fr"),
            ["fields"] = new JsonArray("title"),
            ["resources"] = new JsonArray("b", "a", "b"),
            ["sites"] = new JsonArray(DefaultSite.Id.ToString("D").ToUpperInvariant()),
        });
        var draft = new RoleDraft { Name = "Translators" };
        draft.Grants.Add(sent);
        draft.Grants.Add(new GrantDraft("schema.read", []));
        var body = draft.ToJson();
        body["siteId"] = null;
        var made = await super.Api.CreateRoleAsync(body, Token);

        var read = RoleDraft.From(await super.Api.GetRoleAsync(made.Id, Token));
        read.Grants.Select(grant => grant.Identity).ShouldBe(draft.Grants.Select(grant => grant.Identity), ignoreOrder: true);
        read.Grants.Single(grant => grant.Permission == "schema.read").Constraints.ShouldBeNull();

        var rules = await super.Api.GetRowRulesAsync(Token);
        rules.ShouldContain(rule => rule.Name == BuiltInRoles.OwnEntriesRule);
        read.Grants.Single(grant => grant.Permission == "content.article.read").Constraints!["rowRule"] = "not-registered";
        var refused = await Should.ThrowAsync<ApiProblemException>(() => super.Api.UpdateRoleAsync(made.Id, read.ToJson(), Token));
        refused.Problem.Errors.ShouldContain(error => error.Code == RoleProblems.InvalidGrant);
        var index = read.Grants.FindIndex(grant => grant.Permission == "content.article.read");
        RoleProblems.RefusedGrants(refused).ShouldBe([(index, "constraints.rowRule")]);
    }

    // The Public role of a site is what anyone reads without signing in (row 4.8).
    [Fact]
    public async Task WhatThePublicRoleGrantsIsReadWithoutSigningIn()
    {
        await using var admin = new AdminHost();
        using var super = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        using var anonymous = new HttpClient(admin.ServerHandler()) { BaseAddress = new Uri("http://localhost/") };
        using (var refused = await anonymous.GetAsync(new Uri("api/delivery/v1/article", UriKind.Relative), Token))
        {
            // A type the caller may not read is not found, so that its existence does not show.
            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var roles = await super.Api.GetRolesAsync(Token);
        var visitor = roles.FirstOrDefault(role => role.SiteId == DefaultSite.Id && RoleScope.IsPublic(role.Id, role.SiteId, role.Name))
            ?? roles.Single(role => role.Id == RoleScope.SeededPublic);
        var draft = RoleDraft.From(await super.Api.GetRoleAsync(visitor.Id, Token));
        draft.Grants.Add(new GrantDraft("content.article.read", null));
        await super.Api.UpdateRoleAsync(visitor.Id, draft.ToJson(), Token);

        using var read = await anonymous.GetAsync(new Uri("api/delivery/v1/article", UriKind.Relative), Token);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static JsonNode Body(JsonNode paths, string path, string method) =>
        paths[path]![method]!["requestBody"]!["content"]!["application/json"]!["schema"]!;

    private static string[] Properties(JsonNode schema, JsonObject document) =>
        [.. Resolve(schema, document)!["properties"]!.AsObject().Select(property => property.Key)];

    // A $ref, or the one alternative of a nullable oneOf that is not null.
    private static JsonNode? Resolve(JsonNode? schema, JsonObject document)
    {
        while (true)
        {
            if (schema?["$ref"]?.GetValue<string>() is { } reference)
            {
                schema = document["components"]!["schemas"]![reference["#/components/schemas/".Length..]];
                continue;
            }

            if (schema?["oneOf"] is JsonArray alternatives)
            {
                schema = alternatives.Single(alternative => alternative?["type"]?.GetValue<string>() != "null");
                continue;
            }

            return schema;
        }
    }
}
