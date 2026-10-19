using System.Diagnostics;
using FluentCMS.Core.Content;
using FluentCMS.Core.Delivery;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Content;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.Content.EntryScenario;

namespace FluentCMS.IntegrationTests.Delivery;

// Measured alone: the collection runs after the parallel ones, so the rest of the suite does not load the machine meanwhile.
[CollectionDefinition(nameof(DeliveryPerformanceTests), DisableParallelization = true)]
public sealed class DeliveryPerformanceCollection;

[Collection(nameof(DeliveryPerformanceTests))]
public sealed class DeliveryPerformanceTests
{
    private const int Products = 50;

    private const int Entries = 10_000;

    private const int Reads = 200;

    // PERF-03: one resolved entry, a product page whose price comes from its product, reads under 100 ms at p95 with 10,000 entries
    // loaded. The pages are written straight to the tables, as the directory would store them, so loading them takes seconds.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task One_resolved_entry_reads_under_100_ms_at_p95_with_10000_entries_loaded(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateReferenceModelAsync();
        var products = new List<Guid>();
        for (var number = 0; number < Products; number++)
        {
            var product = (await scenario.CreateAsync("product", $$"""{"name": {{Fact($"\"Product {number}\"")}}, "price": {{Fact($"{number + 10}")}}}""")).Value.Id;
            products.Add(product);
        }

        var pages = await LoadPagesAsync(scenario, products, Entries - Products);
        await scenario.Schema.QueryAsync(context => context.Entries
            .Where(row => products.Contains(row.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, 1)));

        var random = new Random(2026);
        var timings = new List<double>();
        for (var read = 0; read < Reads + 10; read++)
        {
            var page = pages[random.Next(pages.Count)];
            await using var scope = scenario.Services.CreateAsyncScope();
            var content = scope.ServiceProvider.GetRequiredService<IPublishedContent>();

            var watch = Stopwatch.StartNew();
            var item = await content.GetAsync(page);
            watch.Stop();

            Assert.True(item.IsSuccess);
            Assert.Empty(item.Value.Unresolved);

            // The first reads warm the connection pool and the query caches, which every running host has done long before.
            if (read >= 10)
            {
                timings.Add(watch.Elapsed.TotalMilliseconds);
            }
        }

        Assert.Equal(Entries, await scenario.Schema.QueryAsync(context => context.Entries.CountAsync()));
        var p95 = timings.Order().ElementAt((int)Math.Ceiling(timings.Count * 0.95) - 1);
        Assert.True(p95 < 100, $"p95 of one resolved entry read was {p95:F1} ms on {provider}.");
    }

    private static async Task<List<Guid>> LoadPagesAsync(EntryScenario scenario, List<Guid> products, int count)
    {
        var type = await scenario.Schema.QueryAsync(context => context.ContentTypes.SingleAsync(row => row.Slug == "product-page"));
        var pages = new List<Guid>();
        var now = DateTimeOffset.UtcNow;

        for (var batch = 0; batch < count; batch += 1000)
        {
            await scenario.Schema.QueryAsync(async context =>
            {
                for (var number = batch; number < Math.Min(batch + 1000, count); number++)
                {
                    var id = Guid.CreateVersion7();
                    var product = products[number % products.Count];
                    pages.Add(id);
                    context.Entries.Add(new EntryRow
                    {
                        Id = id,
                        SiteId = Site.Default.Id,
                        ContentTypeId = type.Id,
                        LatestVersion = 1,
                        PublishedVersion = 1,
                        State = EntryState.Published,
                    });
                    context.EntryVersions.Add(new EntryVersionRow
                    {
                        Id = Guid.CreateVersion7(),
                        EntryId = id,
                        Version = 1,
                        ContentTypeVersion = type.Version,
                        Fields = $$"""{"product": "{{product}}", "price": "{{product}}", "headline": "Page {{number}}"}""",
                        CreatedAt = now,
                    });
                    context.EntryReferences.Add(new EntryReferenceRow
                    {
                        Id = Guid.CreateVersion7(), EntryId = id, Version = 1, Path = "/product", TargetKind = ReferenceKind.Entry, TargetId = product,
                    });
                    context.EntryReferences.Add(new EntryReferenceRow
                    {
                        Id = Guid.CreateVersion7(), EntryId = id, Version = 1, Path = "/price", TargetKind = ReferenceKind.Entry, TargetId = product, TargetField = "price",
                    });
                }

                return await context.SaveChangesAsync();
            });
        }

        return pages;
    }
}
