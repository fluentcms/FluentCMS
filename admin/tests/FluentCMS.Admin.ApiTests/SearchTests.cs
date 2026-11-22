using System.Text.Json.Nodes;
using FluentCMS.Admin.Administration.Jobs;
using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Plugins.Search.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The search half's call (spec 5.7) against the server half: its record held to the OpenAPI document, a rebuild of one type
// whose job a caller who holds jobs.manage for the instance reads, and the refusals of a type that is not searchable, of a
// name that is no type, and of a caller who holds search.manage in no site.
public sealed class SearchTests
{
    private const string Prefix = "/api/management/v1/";

    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task TheRecordOfTheHalfMatchesTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        List<string> gaps = [];

        OpenApiContractTests.Check(SearchJsonContext.Default.SearchRebuildQueued, OpenApiContractTests.Answer(document["paths"]!, Prefix + "search/rebuild", "post", "202"), "search/rebuild", document, gaps);

        gaps.ShouldBeEmpty();
        // The body is optional: null, or the request.
        var schema = document["paths"]![Prefix + "search/rebuild"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!;
        var reference = schema["oneOf"]!.AsArray().Select(item => item!["$ref"]?.GetValue<string>()).Single(item => item is not null)!;
        document["components"]!["schemas"]![reference.Split('/')[^1]]!["properties"]!.AsObject().Select(member => member.Key).ShouldBe(["type"]);
    }

    [Fact]
    public async Task ARebuildOfOneTypeIsStartedAndItsJobIsReadByACallerWhoMayReadJobs()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var api = client.Api;

        var id = await api.RebuildAsync("article", Token);
        var job = await api.GetJobAsync(id, Token);
        (job.Type, job.SiteId).ShouldBe(("search.rebuild", DefaultSite.Id));
        (await api.RebuildAsync(null, Token)).ShouldNotBe(id);

        SearchProblems.Has(await Should.ThrowAsync<ApiProblemException>(() => api.RebuildAsync("tag", Token)), SearchProblems.NotSearchable).ShouldBeTrue();
        SearchProblems.Has(await Should.ThrowAsync<ApiProblemException>(() => api.RebuildAsync("no-such-type", Token)), SearchProblems.InvalidRebuild).ShouldBeTrue();
    }

    [Fact]
    public async Task ACallerWithoutSearchManageIsRefusedTheRebuild()
    {
        await using var admin = new AdminHost();
        using var super = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var role = new JsonObject
        {
            ["name"] = "Reader of the schema",
            ["siteId"] = DefaultSite.Id,
            ["grants"] = new JsonArray(new JsonObject { ["permission"] = CorePermissions.SchemaRead }),
        };
        await super.Api.SendAsync(new ApiRequest(HttpMethod.Post, "roles") { Body = role }, Token);
        using var client = await admin.SignInAsync("Reader of the schema");

        SearchProblems.IsForbidden(await Should.ThrowAsync<ApiProblemException>(() => client.Api.RebuildAsync(null, Token))).ShouldBeTrue();
    }
}
