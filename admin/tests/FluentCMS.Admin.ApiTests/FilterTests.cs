using FluentCMS.Admin.Api;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Lists;
using FluentCMS.Core.Authorization;
using IFieldTypeRegistry = FluentCMS.Schema.IFieldTypeRegistry;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The filters, the search and the badges of A-48 on the Blog sample's articles, as each role the first release is used with
// reads them: the admin's own query writer against the server's grammar (spec 4.4).
public sealed class FilterTests
{
    private static CancellationToken Token => AdminHost.Token;

    [Theory]
    [InlineData(BuiltInRoles.SuperAdminName)]
    [InlineData(BuiltInRoles.EditorName)]
    public async Task AnArticleIsFoundByAFilterOnATextADateAndASelectField(string role)
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(role);
        var view = await ViewAsync(client);

        (await TitlesAsync(client, view, "filter[title][contains]=Typed")).ShouldBe(["Typed content, no magic"]);
        (await TitlesAsync(client, view, "filter[category][eq]=opinion")).ShouldBe(["Typed content, no magic"]);
        (await TitlesAsync(client, view, "filter[publishedOn][gt]=2000-01-01T00%3A00%3A00Z")).ShouldBe(["Welcome to FluentCMS"]);
        (await TitlesAsync(client, view, "filter[category][in]=news,video&filter[publishedOn][null]=true"))
            .ShouldBe(["A tour of the blog sample", "FluentCMS 1.0 release notes"], ignoreOrder: true);
    }

    // The index is written from the entry events, which the outbox delivers a moment after the seed.
    [Theory]
    [InlineData(BuiltInRoles.SuperAdminName)]
    [InlineData(BuiltInRoles.EditorName)]
    public async Task AnArticleIsFoundBySearchWhereTheTypeIsSearchable(string role)
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(role);
        var view = await ViewAsync(client);
        var access = await client.Client.GetMyAccessAsync(null, null, Token);

        view.Searchable.ShouldBeTrue();
        access.Search.ShouldBeTrue();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        IReadOnlyList<string> found;
        do
        {
            found = await TitlesAsync(client, view, "q=tour");
        }
        while (found.Count == 0 && DateTimeOffset.UtcNow < deadline && await Delay());

        found.ShouldBe(["A tour of the blog sample"]);
        (await TitlesAsync(client, view, "q=tour&filter[category][eq]=news")).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(BuiltInRoles.SuperAdminName)]
    [InlineData(BuiltInRoles.EditorName)]
    public async Task EveryLocaleGivesTheStatusOfEachLocaleAndThePendingSchedule(string role)
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(role);
        var view = await ViewAsync(client);
        var site = (await client.Client.GetMySitesAsync(Token)).Single(item => item.IsDefault);
        var locales = site.Locales.Where(locale => locale.Enabled).OrderBy(locale => locale.Position).Select(locale => locale.Code).ToList();
        var query = view.Query(view.Choose(["status"]), view.ParseSort("title"), 1, 25, everyLocale: true);

        var page = await client.Client.ListEntriesAsync("article", query, Token);
        var (rows, byId) = EveryLocaleRows.Read(page.Items, site.DefaultLocale, locales);

        locales.ShouldBe(["en", "fr"]);
        page.Total.ShouldBe(4);
        rows.Select(Title).ShouldBe(["A tour of the blog sample", "FluentCMS 1.0 release notes", "Typed content, no magic", "Welcome to FluentCMS"]);
        var typed = byId[rows.Single(row => Title(row) == "Typed content, no magic").Id];
        typed.Keys.ShouldBe(["en"]);
        typed["en"].Status.ShouldBe("published");
        var welcome = byId[rows.Single(row => Title(row) == "Welcome to FluentCMS").Id];
        welcome.Keys.ShouldBe(["en", "fr"], ignoreOrder: true);
        welcome["fr"].Status.ShouldBe("published");
        Title(welcome["fr"]).ShouldBe("Bienvenue dans FluentCMS");
        var notes = byId[rows.Single(row => Title(row) == "FluentCMS 1.0 release notes").Id]["en"];
        notes.Status.ShouldBe("draft");
        notes.ScheduledPublishAt.ShouldNotBeNull();
        notes.ScheduledUnpublishAt.ShouldBeNull();
    }

    // Every operator the admin offers on a core field type is one the server takes on it; component has none on either side,
    // and media is a plugin's, whose admin half names its operators (A-18).
    [Fact]
    public async Task EveryOperatorTheAdminOffersIsOneTheServerTakes()
    {
        await using var admin = new AdminHost();
        var types = admin.Host.Services.GetRequiredService<IFieldTypeRegistry>().All;
        var filters = new ListFilterRegistry();

        foreach (var type in types)
        {
            var offered = filters.Find(new SchemaFieldDescription { Name = "value", Type = type.Name, Validation = new FieldValidationDescription() });
            var taken = type.FilterOperators.Select(item => item.ToString()).ToHashSet(StringComparer.Ordinal);
            if (offered is null)
            {
                type.Name.ShouldBeOneOf("component", "media");
                continue;
            }

            offered.Operators.Select(item => item.ToString()).ShouldBeSubsetOf(taken, $"the operators of {type.Name}");
        }

        types.Count.ShouldBeGreaterThan(15);
    }

    private static async Task<ListView> ViewAsync(AdminClient client)
    {
        var schema = await client.Metadata.GetSchemaAsync(Token);
        return ListView.For(schema.Types.Single(type => type.Name == "article"), schema, new ListCellRegistry(), new ListFilterRegistry());
    }

    // The titles of the articles the address finds, in English.
    private static async Task<IReadOnlyList<string>> TitlesAsync(AdminClient client, ListView view, string address)
    {
        var filters = ListFilters.Parse(address, view.Filters);
        filters.Count.ShouldBe(address.Split('&').Count(part => part.StartsWith(ListFilters.Prefix, StringComparison.Ordinal)), address);
        var query = view.Query(view.Choose(["status"]), view.ParseSort("title"), 1, 25, filters, ListFilters.ParseSearch(ListFilters.SearchOf(address))) with { Locale = "en" };
        var page = await client.Client.ListEntriesAsync("article", query, Token);
        return [.. page.Items.Select(Title)];
    }

    private static string Title(ContentEntry entry) => entry.Fields["title"]!.GetValue<string>();

    private static async Task<bool> Delay()
    {
        await Task.Delay(200, Token);
        return true;
    }
}
