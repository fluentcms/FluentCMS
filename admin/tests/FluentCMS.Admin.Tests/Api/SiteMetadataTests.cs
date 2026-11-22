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

    // The server narrows the schema by the locale, so it is read in the content locale and again when that changes; the
    // plugins are the site's, read in none.
    [Fact]
    public async Task TheSchemaIsReadInTheContentLocaleAndThePluginsInNone()
    {
        using var harness = new ApiHarness();
        harness.Server.Answer(HttpStatusCode.OK, Schema).Answer(HttpStatusCode.OK, Plugins).Answer(HttpStatusCode.OK, Schema).Answer(HttpStatusCode.OK, Schema);

        await harness.Metadata.GetSchemaAsync(Token);
        await harness.Metadata.GetPluginsAsync(Token);
        harness.Session.Locale = "fr";
        await harness.Metadata.GetSchemaAsync(Token);
        await harness.Metadata.GetPluginsAsync(Token);
        await harness.Metadata.GetSchemaAsync(Token);
        harness.Session.Site = "second";
        harness.Session.Locale = null;
        await harness.Metadata.GetSchemaAsync(Token);

        harness.Server.Requests.Select(request => (request.Uri.AbsolutePath[19..], request.Header("X-Cms-Site"), request.Header("X-Cms-Locale"))).ShouldBe(
        [
            ("schema", null, null),
            ("plugins", null, null),
            ("schema", null, "fr"),
            ("schema", "second", null),
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

    // A read the shell gave up on goes on until the client's timeout; once forgotten, the next caller sends a new one rather
    // than wait for it. A read that ended is kept.
    [Fact]
    public async Task APendingReadThatIsForgottenIsSentAgain()
    {
        using var harness = new ApiHarness();
        harness.Server.Answer(HttpStatusCode.OK, Schema);
        await harness.Metadata.GetSchemaAsync(Token);
        harness.Server.Hold = new TaskCompletionSource().Task;
        var hung = harness.Metadata.GetPluginsAsync(Token);
        harness.Server.Requests.Count.ShouldBe(2);

        harness.Metadata.ForgetPending();
        harness.Server.Hold = null;
        harness.Server.Answer(HttpStatusCode.OK, Plugins);
        var plugins = await harness.Metadata.GetPluginsAsync(Token);
        await harness.Metadata.GetSchemaAsync(Token);

        plugins.ShouldBeEmpty();
        hung.IsCompleted.ShouldBeFalse();
        harness.Server.Requests.Select(request => request.Uri.AbsolutePath[19..]).ShouldBe(["schema", "plugins", "plugins"]);
    }

    private static async Task ReadTwiceAsync(ApiHarness harness)
    {
        await harness.Metadata.GetSchemaAsync(Token);
        await harness.Metadata.GetPluginsAsync(Token);
        await harness.Metadata.GetSchemaAsync(Token);
        await harness.Metadata.GetPluginsAsync(Token);
    }
}
