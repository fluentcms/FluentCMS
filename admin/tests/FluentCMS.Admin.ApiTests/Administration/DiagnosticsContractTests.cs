using System.Text.Json.Nodes;
using FluentCMS.Admin.Administration.Diagnostics;
using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests.Administration;

// The records of the diagnostics area held to the reference host's OpenAPI document (spec 7), and its calls on the test host:
// the startup report, with the insecure defaults of a test host, and the health of every check, both refused to a caller who
// holds system.read through a role of a site.
public sealed class DiagnosticsContractTests
{
    private const string Prefix = "/api/management/v1/";

    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task TheRecordsOfTheDiagnosticsAreaMatchTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        var paths = document["paths"]!;
        var records = DiagnosticsJsonContext.Default;
        List<string> gaps = [];

        void Check(System.Text.Json.Serialization.Metadata.JsonTypeInfo record, string path) =>
            OpenApiContractTests.Check(record, OpenApiContractTests.Answer(paths, Prefix + path, "get"), path, document, gaps);

        Check(records.StartupReportRecord, "system/diagnostics");
        Check(records.HealthRecord, "system/health");

        gaps.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheStartupReportShowsTheInsecureDefaultsOfTheTestHostAndTheHealthEveryCheck()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var api = client.Api;

        var report = await api.GetStartupReportAsync(Token);
        report.Failure.ShouldBeNull();
        report.Version.ShouldNotBeNullOrEmpty();
        report.DataProvider.Provider.ShouldNotBeNullOrEmpty();
        report.InsecureDefaults.ShouldNotBeEmpty();
        report.InsecureDefaults.ShouldAllBe(item => item.Id.Length > 0 && item.Message.Length > 0);
        report.Plugins.Select(plugin => plugin.Name).ShouldContain("search");
        report.ProviderSlots.ShouldNotBeEmpty();

        var health = await api.GetHealthAsync(Token);
        health.Status.ShouldBeOneOf("Healthy", "Degraded", "Unhealthy");
        health.Checks.ShouldNotBeEmpty();
        health.Checks.ShouldAllBe(check => check.Name.Length > 0);
    }

    [Fact]
    public async Task ACallerWhoHoldsSystemReadThroughARoleOfASiteIsRefusedBoth()
    {
        await using var admin = new AdminHost();
        using var super = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var role = new JsonObject
        {
            ["name"] = "Site observer",
            ["siteId"] = DefaultSite.Id,
            ["grants"] = new JsonArray(new JsonObject { ["permission"] = CorePermissions.SystemRead }),
        };
        await super.Api.SendAsync(new ApiRequest(HttpMethod.Post, "roles") { Body = role }, Token);
        using var client = await admin.SignInAsync("Site observer");

        (await Should.ThrowAsync<ApiProblemException>(() => client.Api.GetStartupReportAsync(Token))).Problem.Status.ShouldBe(403);
        (await Should.ThrowAsync<ApiProblemException>(() => client.Api.GetHealthAsync(Token))).Problem.Status.ShouldBe(403);
    }
}
