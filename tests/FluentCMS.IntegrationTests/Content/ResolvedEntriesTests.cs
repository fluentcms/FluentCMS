using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.Content.EntryScenario;
using static FluentCMS.IntegrationTests.Schema.SchemaScenario;

namespace FluentCMS.IntegrationTests.Content;

public sealed class ResolvedEntriesTests
{
    // S2 in the F-CNT acceptance: the page stores the product, and its view shows the product's price as published now.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_page_shows_the_current_published_price_of_its_product(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var product = await CreateProductAsync(scenario, "19.99");
        await PublishAsync(scenario, product, 1);
        var page = (await scenario.CreateAsync("product-page", $$"""{"product": "{{product}}", "price": "{{product}}", "headline": "Widget"}""")).Value.Id;

        var first = await ResolveAsync(scenario, page);

        AssertSameFields($$"""{"product": "{{product}}", "price": 19.99, "headline": "Widget"}""", first.Fields);
        Assert.Empty(first.Unresolved);
        Assert.Equal(("product-page", 1), (first.ContentType, first.Version));

        // A new draft of the product changes nothing until it is published.
        await scenario.UpdateAsync(product, 1, $$"""{"name": {{Fact("\"Widget\"")}}, "price": {{Fact("24.99")}}}""");
        AssertSameFields($$"""{"product": "{{product}}", "price": 19.99, "headline": "Widget"}""", (await ResolveAsync(scenario, page)).Fields);

        await PublishAsync(scenario, product, 2);
        AssertSameFields($$"""{"product": "{{product}}", "price": 24.99, "headline": "Widget"}""", (await ResolveAsync(scenario, page)).Fields);

        var stored = (await scenario.RunAsync(directory => directory.GetAsync(page))).Value;
        AssertSameFields($$"""{"product": "{{product}}", "price": "{{product}}", "headline": "Widget"}""", stored.Latest.Fields);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_reference_without_a_published_value_is_null_and_says_why(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        await scenario.Schema.CreateAsync("spec-page", Reference("value", "spec", "value"), Reference("unit", "spec", "unit"));
        var unpublished = (await scenario.CreateAsync("spec", $$"""{"name": {{Fact("\"Width\"")}}, "value": {{Fact("\"10\"")}}}""")).Value.Id;
        var withoutUnit = (await scenario.CreateAsync("spec", $$"""{"name": {{Fact("\"Height\"")}}, "value": {{Fact("\"20\"")}}}""")).Value.Id;
        await PublishAsync(scenario, withoutUnit, 1);
        var page = (await scenario.CreateAsync("spec-page", $$"""{"value": "{{unpublished}}", "unit": "{{withoutUnit}}"}""")).Value.Id;

        var resolved = await ResolveAsync(scenario, page);

        AssertSameFields("""{"value": null, "unit": null}""", resolved.Fields);
        Assert.Equal(
            [
                new UnresolvedReference("/unit", withoutUnit, "unit", UnresolvedReason.NoValue),
                new UnresolvedReference("/value", unpublished, "value", UnresolvedReason.NotPublished),
            ],
            resolved.Unresolved);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task References_in_lists_and_facts_resolve_at_their_paths_and_whole_entries_stay_ids(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        await scenario.Schema.CreateEntityAsync("bundle", Field("name", "text"), Reference("price", "product", "price"));
        var (cheap, dear) = (await CreateProductAsync(scenario, "5"), await CreateProductAsync(scenario, "500"));
        await PublishAsync(scenario, cheap, 1);
        await PublishAsync(scenario, dear, 1);
        var comparison = (await scenario.CreateAsync("comparison-page", $$"""
            {"products": ["{{cheap}}", "{{dear}}"], "prices": ["{{cheap}}", "{{dear}}"], "verdict": "Cheap wins."}
            """)).Value.Id;
        var bundle = (await scenario.CreateAsync("bundle", $$"""{"price": {{Fact($"\"{dear}\"")}}}""")).Value.Id;

        AssertSameFields(
            $$"""{"products": ["{{cheap}}", "{{dear}}"], "prices": [5, 500], "verdict": "Cheap wins."}""",
            (await ResolveAsync(scenario, comparison)).Fields);
        AssertSameFields($$"""{"price": {{Fact("500")}}}""", (await ResolveAsync(scenario, bundle)).Fields);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_earlier_version_resolves_and_unknown_entries_and_versions_fail_without_an_event(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var product = await CreateProductAsync(scenario, "19.99");
        await PublishAsync(scenario, product, 1);
        var page = (await scenario.CreateAsync("product-page", $$"""{"price": "{{product}}", "headline": "With price"}""")).Value.Id;
        await scenario.UpdateAsync(page, 1, """{"headline": "Without price"}""");
        var events = (await scenario.EntryEventsAsync()).Count;

        AssertSameFields("""{"price": 19.99, "headline": "With price"}""", (await ResolveAsync(scenario, page, 1)).Fields);
        AssertSameFields("""{"headline": "Without price"}""", (await ResolveAsync(scenario, page)).Fields);

        var unknownEntry = await ResolveResultAsync(scenario, Guid.CreateVersion7(), null);
        var unknownVersion = await ResolveResultAsync(scenario, page, 3);

        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single(unknownEntry.Errors).Code);
        Assert.Equal(ContentErrorCodes.VersionNotFound, Assert.Single(unknownVersion.Errors).Code);
        Assert.Equal(events, (await scenario.EntryEventsAsync()).Count);
    }

    private static async Task<EntryScenario> CreateAsync(string provider)
    {
        var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateReferenceModelAsync();
        return scenario;
    }

    private static async Task<Guid> CreateProductAsync(EntryScenario scenario, string price)
    {
        var created = await scenario.CreateAsync("product", $$"""{"name": {{Fact("\"Widget\"")}}, "price": {{Fact(price)}}}""");
        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(error => error.Message)));
        return created.Value.Id;
    }

    // Publish moves this pointer once entry operations reach change sets (F-CHG); until then the tests move it themselves.
    private static Task<int> PublishAsync(EntryScenario scenario, Guid entry, int version)
    {
        return scenario.Schema.QueryAsync(context => context.Entries
            .Where(row => row.Id == entry)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, version)));
    }

    private static async Task<ResolvedEntry> ResolveAsync(EntryScenario scenario, Guid entry, int? version = null)
    {
        var resolved = await ResolveResultAsync(scenario, entry, version);
        Assert.True(resolved.IsSuccess, string.Join("; ", resolved.Errors.Select(error => error.Message)));
        return resolved.Value;
    }

    private static async Task<Result<ResolvedEntry>> ResolveResultAsync(EntryScenario scenario, Guid entry, int? version)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IResolvedEntries>().ResolveAsync(entry, version);
    }
}
