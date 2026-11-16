using System.Net;
using FluentCMS.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Api;

public sealed class SiteMetadataTests
{
    private const string Schema = """{"types":[{"name":"article","kind":"collection","fields":[{"name":"title","type":"text","validation":{"required":true}}]}],"components":[],"blocks":[]}""";
    private const string Plugins = """{"items":[]}""";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheSchemaAndPluginsAreReadOncePerSiteAndAgainAfterASwitch()
    {
        using var harness = new ApiHarness();
        harness.Session.Site = "blog";
        for (var site = 0; site < 3; site++)
        {
            harness.Server.Answer(HttpStatusCode.OK, Schema).Answer(HttpStatusCode.OK, Plugins);
        }

        await ReadTwiceAsync(harness);
        harness.Session.Site = "shop";
        await ReadTwiceAsync(harness);
        harness.Session.Site = "blog";
        await ReadTwiceAsync(harness);

        harness.Server.Requests.Select(request => $"{request.Uri.AbsolutePath} {request.Header("X-Cms-Site")}").ShouldBe(
        [
            "/api/management/v1/schema blog",
            "/api/management/v1/plugins blog",
            "/api/management/v1/schema shop",
            "/api/management/v1/plugins shop",
            "/api/management/v1/schema blog",
            "/api/management/v1/plugins blog",
        ]);
    }

    [Fact]
    public async Task AReadThatFailedIsNotKept()
    {
        using var harness = new ApiHarness();
        harness.Server.Problem(HttpStatusCode.Forbidden, """{"status":403,"code":"FCMS2001"}""").Answer(HttpStatusCode.OK, Schema);

        await Should.ThrowAsync<ApiProblemException>(() => harness.Metadata.GetSchemaAsync(Token));
        var schema = await harness.Metadata.GetSchemaAsync(Token);

        schema.Types.Single().Fields.Single().Validation.Required.ShouldBeTrue();
        harness.Server.Requests.Count.ShouldBe(2);
    }

    private static async Task ReadTwiceAsync(ApiHarness harness)
    {
        await harness.Metadata.GetSchemaAsync(Token);
        await harness.Metadata.GetPluginsAsync(Token);
        await harness.Metadata.GetSchemaAsync(Token);
        await harness.Metadata.GetPluginsAsync(Token);
    }
}
