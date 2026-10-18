using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Content;

public sealed class WhereUsedTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_page_that_shows_a_products_price_is_where_the_price_and_the_product_are_used(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var product = await CreateProductAsync(scenario, "Widget");
        var other = await CreateProductAsync(scenario, "Gadget");

        var page = (await scenario.CreateAsync("product-page", $$"""
            {"product": "{{product}}", "price": "{{product}}", "headline": "The Widget"}
            """)).Value;
        var comparison = (await scenario.CreateAsync("comparison-page", $$"""
            {"products": ["{{other}}", "{{product}}"], "prices": ["{{other}}", "{{product}}"]}
            """)).Value;

        Assert.Equal(
            new[] { Usage(page.Id, "product-page", "/price"), Usage(comparison.Id, "comparison-page", "/prices/1") }.OrderBy(usage => usage.EntryId),
            await RunAsync(scenario, whereUsed => whereUsed.ForEntityFieldAsync(product, "price")),
            UsageComparer.Instance);
        Assert.Equal(
            new[] { Usage(page.Id, "product-page", "/price", "/product"), Usage(comparison.Id, "comparison-page", "/prices/1", "/products/1") }
                .OrderBy(usage => usage.EntryId),
            await RunAsync(scenario, whereUsed => whereUsed.ForEntryAsync(product)),
            UsageComparer.Instance);
        Assert.Empty(await RunAsync(scenario, whereUsed => whereUsed.ForEntityFieldAsync(product, "name")));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_reference_to_a_missing_entry_or_one_of_another_type_fails_and_writes_nothing(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var product = await CreateProductAsync(scenario, "Widget");
        var missing = Guid.CreateVersion7();
        var post = (await scenario.CreateAsync("blog-post", """{"title": "News"}""")).Value.Id;

        var result = await scenario.CreateAsync("comparison-page", $$"""
            {"products": ["{{product}}", "{{missing}}"], "prices": ["{{post}}"]}
            """);

        Assert.Equal(
            [
                (ContentErrorCodes.ReferenceNotFound, $"Field 'products': /products/1 points at {missing}, which is not an entry in this site."),
                (ContentErrorCodes.ReferenceTypeMismatch,
                    $"Field 'prices': /prices/0 points at {post}, which is a 'blog-post', but the field references 'product'."),
            ],
            result.Errors.Select(error => (error.Code, error.Message)));
        await scenario.AssertStoredAsync(entries: 2, versions: 2, events: 2);

        var update = await scenario.UpdateAsync(post, 1, $$"""{"title": "News", "related": ["{{missing}}"]}""");

        Assert.Equal(ContentErrorCodes.ReferenceNotFound, Assert.Single(update.Errors).Code);
        await scenario.AssertStoredAsync(entries: 2, versions: 2, events: 2);
        Assert.Empty(await scenario.Schema.QueryAsync(context => context.EntryReferences.Where(row => row.EntryId == post).ToListAsync()));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Only_the_latest_draft_and_the_published_version_count_as_uses(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var product = await CreateProductAsync(scenario, "Widget");
        var page = (await scenario.CreateAsync("product-page", $$"""{"price": "{{product}}", "headline": "Widget"}""")).Value.Id;

        await scenario.UpdateAsync(page, 1, """{"headline": "No price any more"}""");

        Assert.Empty(await RunAsync(scenario, whereUsed => whereUsed.ForEntityFieldAsync(product, "price")));

        // Publish moves this pointer (F-CHG); the index keeps every version's references, so the published version counts again.
        await scenario.Schema.QueryAsync(context => context.Entries
            .Where(row => row.Id == page)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, 1)));

        var usage = Assert.Single(await RunAsync(scenario, whereUsed => whereUsed.ForEntityFieldAsync(product, "price")));
        Assert.Equal(new EntryUsage(page, "product-page", 1, IsLatest: false, IsPublished: true, ["/price"]), usage, UsageComparer.Instance);

        var rows = await scenario.Schema.QueryAsync(context => context.EntryReferences.Where(row => row.EntryId == page).ToListAsync());
        Assert.Equal(1, Assert.Single(rows).Version);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task References_inside_facts_and_to_media_are_indexed_with_their_paths(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var category = (await scenario.CreateAsync("category", $$"""{"name": {{Fact("\"Tools\"")}}}""")).Value.Id;
        var hero = Guid.CreateVersion7();

        var product = (await scenario.CreateAsync("product", $$"""
            {"name": {{Fact("\"Widget\"")}}, "price": {{Fact("10")}}, "category": {{Fact($"\"{category}\"")}}, "hero": {{Fact($"\"{hero}\"")}}}
            """)).Value.Id;

        Assert.Equal(
            [Usage(product, "product", "/category/value")],
            await RunAsync(scenario, whereUsed => whereUsed.ForEntryAsync(category)),
            UsageComparer.Instance);
        Assert.Equal(
            [Usage(product, "product", "/hero/value")],
            await RunAsync(scenario, whereUsed => whereUsed.ForMediaAsync(hero)),
            UsageComparer.Instance);
        Assert.Empty(await RunAsync(scenario, whereUsed => whereUsed.ForMediaAsync(Guid.CreateVersion7())));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Unknown_entries_entries_that_are_not_entities_and_unknown_fields_fail(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var product = await CreateProductAsync(scenario, "Widget");
        var post = (await scenario.CreateAsync("blog-post", """{"title": "News"}""")).Value.Id;

        var unknownEntry = await RunResultAsync(scenario, whereUsed => whereUsed.ForEntryAsync(Guid.CreateVersion7()));
        var unknownEntity = await RunResultAsync(scenario, whereUsed => whereUsed.ForEntityFieldAsync(Guid.CreateVersion7(), "price"));
        var notEntity = await RunResultAsync(scenario, whereUsed => whereUsed.ForEntityFieldAsync(post, "title"));
        var unknownField = await RunResultAsync(scenario, whereUsed => whereUsed.ForEntityFieldAsync(product, "cost"));

        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single(unknownEntry.Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single(unknownEntity.Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotEntity, Assert.Single(notEntity.Errors).Code);
        Assert.Equal(ContentErrorCodes.FieldUnknown, Assert.Single(unknownField.Errors).Code);
        Assert.Empty((await RunResultAsync(scenario, whereUsed => whereUsed.ForEntryAsync(post))).Value);
    }

    private static async Task<EntryScenario> CreateAsync(string provider)
    {
        var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateReferenceModelAsync();
        return scenario;
    }

    private static async Task<Guid> CreateProductAsync(EntryScenario scenario, string name)
    {
        var created = await scenario.CreateAsync("product", $$"""{"name": {{Fact($"\"{name}\"")}}, "price": {{Fact("19.99")}}}""");
        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(error => error.Message)));
        return created.Value.Id;
    }

    private static string Fact(string value)
    {
        return $$"""{"value": {{value}}, "source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null}""";
    }

    private static EntryUsage Usage(Guid entryId, string contentType, params string[] paths)
    {
        return new EntryUsage(entryId, contentType, 1, IsLatest: true, IsPublished: false, paths);
    }

    private static async Task<IReadOnlyList<EntryUsage>> RunAsync(
        EntryScenario scenario,
        Func<IWhereUsed, Task<Result<IReadOnlyList<EntryUsage>>>> query)
    {
        var result = await RunResultAsync(scenario, query);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }

    private static async Task<IReadOnlyList<EntryUsage>> RunAsync(EntryScenario scenario, Func<IWhereUsed, Task<IReadOnlyList<EntryUsage>>> query)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<IWhereUsed>());
    }

    private static async Task<T> RunResultAsync<T>(EntryScenario scenario, Func<IWhereUsed, Task<T>> query)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<IWhereUsed>());
    }

    // A usage's paths are a list, which records compare by reference.
    private sealed class UsageComparer : IEqualityComparer<EntryUsage>
    {
        public static readonly UsageComparer Instance = new();

        public bool Equals(EntryUsage? x, EntryUsage? y)
        {
            return x is not null
                && y is not null
                && (x.EntryId, x.ContentType, x.Version, x.IsLatest, x.IsPublished) == (y.EntryId, y.ContentType, y.Version, y.IsLatest, y.IsPublished)
                && x.Paths.SequenceEqual(y.Paths);
        }

        public int GetHashCode(EntryUsage obj)
        {
            return obj.EntryId.GetHashCode();
        }
    }
}
