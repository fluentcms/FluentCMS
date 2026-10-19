using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Api;
using FluentCMS.Core.Content;
using FluentCMS.Core.Delivery;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Delivery;

public sealed class DeliveryEndpointsTests
{
    private static readonly Guid Page = Guid.CreateVersion7();
    private static readonly Guid Product = Guid.CreateVersion7();

    [Fact]
    public async Task A_list_returns_items_with_their_resolved_fields_and_the_next_cursor()
    {
        var content = new StubContent();
        await using var api = await StubApi.StartAsync(content);

        var response = await api.Client.GetFromJsonAsync<JsonObject>($"/api/v1/content/product-page?limit=1&after={Product}&references={Product}");

        Assert.Equal(new PublishedQuery("product-page", 1, Product, Product), content.Query);
        var item = Assert.Single(response!["items"]!.AsArray())!;
        Assert.Equal((Page.ToString(), "product-page", 3), ((string?)item["id"], (string?)item["type"], (int)item["version"]!));
        Assert.Equal(99, (int)item["fields"]!["price"]!);
        var unresolved = Assert.Single(item["unresolved"]!.AsArray())!;
        Assert.Equal(("/spec", "notPublished"), ((string?)unresolved["path"], (string?)unresolved["reason"]));
        Assert.Equal(Page.ToString(), (string?)response["next"]);
    }

    [Fact]
    public async Task An_entry_reads_by_id_and_a_missing_one_is_a_problem_with_404()
    {
        await using var api = await StubApi.StartAsync(new StubContent());

        var found = await api.Client.GetFromJsonAsync<JsonObject>($"/api/v1/entries/{Page}");
        var missing = await api.Client.GetAsync($"/api/v1/entries/{Guid.CreateVersion7()}");
        var unknownType = await api.Client.GetAsync("/api/v1/content/news");

        Assert.Equal(Page.ToString(), (string?)found!["id"]);
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, ContentErrorCodes.EntryNotFound);
        await AssertProblemAsync(unknownType, HttpStatusCode.NotFound, SchemaErrorCodes.ContentTypeNotFound);
    }

    [Theory]
    [InlineData("limit=ten", DeliveryErrorCodes.LimitInvalid, "'limit'")]
    [InlineData("limit=-1", DeliveryErrorCodes.LimitInvalid, "'limit'")]
    [InlineData("after=page-2", DeliveryErrorCodes.ParameterInvalid, "'after'")]
    [InlineData("references=drill", DeliveryErrorCodes.ParameterInvalid, "'references'")]
    public async Task A_parameter_that_does_not_parse_is_a_problem_with_400_that_names_it(string query, string code, string name)
    {
        var content = new StubContent();
        await using var api = await StubApi.StartAsync(content);

        var response = await api.Client.GetAsync($"/api/v1/content/product-page?{query}");

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
        Assert.Contains(name, (string?)problem["detail"]);
        Assert.Null(content.Query);
    }

    [Fact]
    public async Task The_host_serves_the_api_to_anonymous_readers_from_the_database()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);

        var unknownType = await host.Client.GetAsync("/api/v1/content/product-page");
        var unknownEntry = await host.Client.GetAsync($"/api/v1/entries/{Guid.CreateVersion7()}");
        var badLimit = await host.Client.GetAsync("/api/v1/content/product-page?limit=0");

        await AssertProblemAsync(unknownType, HttpStatusCode.NotFound, SchemaErrorCodes.ContentTypeNotFound);
        await AssertProblemAsync(unknownEntry, HttpStatusCode.NotFound, ContentErrorCodes.EntryNotFound);
        await AssertProblemAsync(badLimit, HttpStatusCode.BadRequest, DeliveryErrorCodes.LimitInvalid);
    }

    private static async Task<JsonObject> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(code, (string?)problem["errors"]![0]!["code"]);
        return problem;
    }

    // Answers like the reader would for one published page, and records the query it was asked.
    private sealed class StubContent : IPublishedContent
    {
        public PublishedQuery? Query { get; private set; }

        public Task<Result<PublishedPage>> ListAsync(PublishedQuery query, CancellationToken cancellationToken = default)
        {
            Query = query;
            return Task.FromResult(query.ContentType == "product-page"
                ? Result<PublishedPage>.Success(new PublishedPage([Item()], Page))
                : Result<PublishedPage>.Failure(SchemaErrorCodes.ContentTypeNotFound, $"There is no content type '{query.ContentType}' in this site."));
        }

        public Task<Result<PublishedItem>> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(id == Page
                ? Result<PublishedItem>.Success(Item())
                : Result<PublishedItem>.Failure(ContentErrorCodes.EntryNotFound, $"There is no published entry {id} in this site."));
        }

        private static PublishedItem Item()
        {
            return new PublishedItem(
                Page,
                "product-page",
                3,
                JsonSerializer.Deserialize<JsonElement>($$"""{"product": "{{Product}}", "price": 99, "spec": null}"""),
                [new UnresolvedReference("/spec", Guid.CreateVersion7(), "value", UnresolvedReason.NotPublished)]);
        }
    }

    // The endpoints alone on a free port, over the content given.
    private sealed class StubApi(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Client { get; } = new() { BaseAddress = new Uri(app.Urls.Single()) };

        public static async Task<StubApi> StartAsync(IPublishedContent content)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(content);
            var app = builder.Build();
            app.MapFluentCmsApi();
            await app.StartAsync();
            return new StubApi(app);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
