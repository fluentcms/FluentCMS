using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.IntegrationTests.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.OpenApi;

// Where MapFluentCmsOpenApi puts the document and the UI, and when it leaves them out.
[Xunit.Collection(ProcessEnvironment.Collection)]
public sealed class OpenApiHostingTests : IAsyncDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CmsWebHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Theory]
    [InlineData("Production", null, false)]
    [InlineData("Staging", null, false)]
    [InlineData("Development", null, true)]
    [InlineData("Production", "true", true)]
    [InlineData("Development", "false", false)]
    public async Task TheyAreOnInDevelopmentAndElsewhereOnlyWhenEnabled(string environment, string? enabled, bool served)
    {
        using var client = await StartAsync(environment, new() { ["FluentCms:OpenApi:Enabled"] = enabled }, app => app.MapFluentCmsOpenApi());

        using var document = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Token);
        using var ui = await client.GetAsync(new Uri("/scalar/", UriKind.Relative), Token);

        document.StatusCode.ShouldBe(served ? HttpStatusCode.OK : HttpStatusCode.NotFound);
        ui.StatusCode.ShouldBe(served ? HttpStatusCode.OK : HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnderThePathBaseTheServerAndTheAuthorizationServerCarryIt()
    {
        using var client = await StartAsync(Environments.Development, new() { ["FluentCms:ReverseProxy:PathBase"] = "/cms" }, app => app.MapFluentCmsOpenApi());

        var document = JsonNode.Parse(await client.GetStringAsync(new Uri("/cms/openapi/v1.json", UriKind.Relative), Token))!;
        var page = await client.GetStringAsync(new Uri("/cms/scalar/", UriKind.Relative), Token);
        using var unprefixed = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Token);

        unprefixed.StatusCode.ShouldBe(HttpStatusCode.OK);
        document["servers"]![0]!["url"]!.GetValue<string>().TrimEnd('/').ShouldBe("http://localhost/cms");
        document["components"]!["securitySchemes"]!["oauth2"]!["flows"]!["authorizationCode"]!["authorizationUrl"]!.GetValue<string>()
            .ShouldBe("http://localhost/cms/connect/authorize");
        page.ShouldContain("\"url\":\"openapi/v1.json\"");
    }

    [Fact]
    public async Task InAGroupTheUiFindsTheDocumentBesideIt()
    {
        using var client = await StartAsync(Environments.Development, [], app => app.MapGroup("/docs").MapFluentCmsOpenApi());

        using var document = await client.GetAsync(new Uri("/docs/openapi/v1.json", UriKind.Relative), Token);
        var page = await client.GetStringAsync(new Uri("/docs/scalar/", UriKind.Relative), Token);
        using var root = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Token);

        document.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.ShouldContain("\"url\":\"docs/openapi/v1.json\"");
        root.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheReturnedBuilderAppliesConventionsToBoth()
    {
        var marker = new object();
        var app = _host.Build(Environments.Development, pipeline: app =>
        {
            app.UseFluentCms();
            app.MapFluentCmsOpenApi().WithMetadata(marker);
        });
        await app.StartAsync(Token);

        var marked = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.Contains(marker))
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToList();

        marked.ShouldContain("/openapi/v1.json");
        marked.ShouldContain(pattern => pattern!.StartsWith("/scalar", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutAddFluentCmsMappingThrows()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        using var app = builder.Build();

        var exception = Should.Throw<FluentCmsConfigurationException>(() => app.MapFluentCmsOpenApi());

        exception.Code.ShouldBe("FCMS1103");
    }

    private async Task<HttpClient> StartAsync(string environment, Dictionary<string, string?> settings, Action<WebApplication> map)
    {
        var app = _host.Build(environment, settings, pipeline: app =>
        {
            app.UseFluentCms();
            map(app);
        });
        await app.StartAsync(Token);
        return app.GetTestClient();
    }
}
