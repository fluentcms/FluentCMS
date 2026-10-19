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
        Assert.Equal($"/api/v1/entries/{Page}.md", (string?)item["markdown"]);
        Assert.Equal(Page.ToString(), (string?)response["next"]);
    }

    [Fact]
    public async Task An_entry_reads_as_markdown_at_its_id()
    {
        await using var api = await StubApi.StartAsync(new StubContent());

        var response = await api.Client.GetAsync($"/api/v1/entries/{Page}.md");
        var missing = await api.Client.GetAsync($"/api/v1/entries/{Guid.CreateVersion7()}.md");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
        var markdown = await response.Content.ReadAsStringAsync();
        Assert.StartsWith($"---\nid: \"{Page}\"\ntype: \"product-page\"\nversion: 3\nurl: \"/api/v1/entries/{Page}.md\"\n---\n\n# A sturdy drill\n", markdown, StringComparison.Ordinal);
        Assert.Contains($"[{Product}](/api/v1/entries/{Product}.md)", markdown, StringComparison.Ordinal);
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, ContentErrorCodes.EntryNotFound);
    }

    [Fact]
    public async Task The_site_index_lists_every_entry_in_json_markdown_and_llms_txt()
    {
        await using var api = await StubApi.StartAsync(new StubContent());

        var json = await api.Client.GetFromJsonAsync<JsonObject>("/api/v1/index");
        var markdown = await api.Client.GetStringAsync("/api/v1/index.md");
        var llms = await api.Client.GetStringAsync("/llms.txt");

        var entries = json!["items"]!.AsArray();
        Assert.Equal([Product.ToString(), Page.ToString()], entries.Select(entry => (string?)entry!["id"]));
        Assert.Equal(("Drill [cordless]", $"/api/v1/entries/{Product}", $"/api/v1/entries/{Product}.md"), ((string?)entries[0]!["title"], (string?)entries[0]!["url"], (string?)entries[0]!["markdown"]));
        Assert.Contains($@"## Product{"\n\n"}- [Drill \[cordless\]](/api/v1/entries/{Product}.md){"\n"}", markdown, StringComparison.Ordinal);
        Assert.Contains($"## Product page\n\n- [Product page {Page}](/api/v1/entries/{Page}.md)\n", markdown, StringComparison.Ordinal);
        Assert.Equal(markdown, llms);
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

    // The admin's not-found page answers every other path of the host, so the API answers each of its own.
    [Theory]
    [InlineData("/api/v1/entries/not-a-guid", HttpStatusCode.BadRequest, DeliveryErrorCodes.ParameterInvalid)]
    [InlineData("/api/v1/entries/not-a-guid.md", HttpStatusCode.BadRequest, DeliveryErrorCodes.ParameterInvalid)]
    [InlineData("/api/v1/entries/", HttpStatusCode.NotFound, DeliveryErrorCodes.EndpointNotFound)]
    [InlineData("/api/v1/content/", HttpStatusCode.NotFound, DeliveryErrorCodes.EndpointNotFound)]
    [InlineData("/api/v1/nothing/here", HttpStatusCode.NotFound, DeliveryErrorCodes.EndpointNotFound)]
    public async Task A_path_or_id_the_api_does_not_know_is_a_problem_never_the_admin_page(string path, HttpStatusCode status, string code)
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);

        await AssertProblemAsync(await host.Client.GetAsync(path), status, code);
    }

    [Fact]
    public async Task A_preview_reads_with_the_bearer_token_and_is_never_cached()
    {
        await using var api = await StubApi.StartAsync(new StubContent());
        using var entry = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/preview/entries/{Page}");
        entry.Headers.Authorization = new("Bearer", StubPreview.Token);
        using var changes = new HttpRequestMessage(HttpMethod.Get, "/api/v1/preview/changes");
        changes.Headers.Authorization = new("Bearer", StubPreview.Token);

        var entryResponse = await api.Client.SendAsync(entry);
        var changesResponse = await api.Client.SendAsync(changes);

        Assert.Equal(HttpStatusCode.OK, entryResponse.StatusCode);
        Assert.True(entryResponse.Headers.CacheControl?.NoStore);
        Assert.Equal(Page.ToString(), (string?)(await entryResponse.Content.ReadFromJsonAsync<JsonObject>())!["id"]);
        var body = (await changesResponse.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal((StubPreview.ChangeSet.ToString(), 1), ((string?)body["changeSet"], body["items"]!.AsArray().Count));
    }

    [Fact]
    public async Task A_preview_without_a_working_token_is_a_problem_with_401()
    {
        await using var api = await StubApi.StartAsync(new StubContent());
        using var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/v1/preview/changes");
        wrong.Headers.Authorization = new("Bearer", "fcmsp_wrong");

        var missing = await api.Client.GetAsync($"/api/v1/preview/entries/{Page}");
        var refused = await api.Client.SendAsync(wrong);

        await AssertProblemAsync(missing, HttpStatusCode.Unauthorized, DeliveryErrorCodes.PreviewTokenInvalid);
        await AssertProblemAsync(refused, HttpStatusCode.Unauthorized, DeliveryErrorCodes.PreviewTokenInvalid);
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

        public Task<IReadOnlyList<PublishedSummary>> IndexAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PublishedSummary>>(
            [
                new PublishedSummary(Product, "product", "Product", 1, "Drill [cordless]"),
                new PublishedSummary(Page, "product-page", "Product page", 3, null),
            ]);
        }

        private static PublishedItem Item()
        {
            return new PublishedItem(
                Page,
                "product-page",
                "Product page",
                new ContentTypeDefinition(
                [
                    new FieldDefinition("product", "reference", settings: JsonSerializer.Deserialize<JsonElement>("""{"target": "product"}""")),
                    new FieldDefinition("price", "number"),
                    new FieldDefinition("headline", "text"),
                ]),
                3,
                JsonSerializer.Deserialize<JsonElement>($$"""{"product": "{{Product}}", "price": 99, "spec": null, "headline": "A sturdy drill"}"""),
                [new UnresolvedReference("/spec", Guid.CreateVersion7(), "value", UnresolvedReason.NotPublished)]);
        }
    }

    // Works for one token only, as a preview of a set that changes the page.
    private sealed class StubPreview : IPreviewContent
    {
        public const string Token = "fcmsp_works";

        public static readonly Guid ChangeSet = Guid.CreateVersion7();

        public async Task<Result<PublishedItem>> GetAsync(string token, Guid entryId, CancellationToken cancellationToken = default)
        {
            return token == Token ? await new StubContent().GetAsync(entryId, cancellationToken) : Refused<PublishedItem>();
        }

        public async Task<Result<PreviewChanges>> ChangesAsync(string token, CancellationToken cancellationToken = default)
        {
            return token == Token
                ? Result<PreviewChanges>.Success(new PreviewChanges(ChangeSet, [(await new StubContent().GetAsync(Page, cancellationToken)).Value]))
                : Refused<PreviewChanges>();
        }

        private static Result<T> Refused<T>()
        {
            return Result<T>.Failure(DeliveryErrorCodes.PreviewTokenInvalid, "The preview token does not work.");
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
            builder.Services.AddSingleton<IPreviewContent>(new StubPreview());
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
