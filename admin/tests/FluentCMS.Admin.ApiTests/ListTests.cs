using System.Text.Json.Nodes;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Lists;
using FluentCMS.Core.Authorization;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The list view of the Blog sample's articles as the admin reads it (spec 4.4), as each role the first release is used with.
public sealed class ListTests
{
    private static readonly string[] Shown = ["status", "updatedAt", "updatedBy", "category", "author", "tags"];

    private static CancellationToken Token => AdminHost.Token;

    [Theory]
    [InlineData(BuiltInRoles.SuperAdminName)]
    [InlineData(BuiltInRoles.EditorName)]
    public async Task TheListReadsOnlyItsColumnsSortedByTwoKeysOnAPageWithATotal(string role)
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(role);
        var schema = await client.Metadata.GetSchemaAsync(Token);
        var view = ListView.For(schema.Types.Single(type => type.Name == "article"), schema, new ListCellRegistry());
        var shown = view.Choose(Shown);
        var query = view.Query(shown, view.ParseSort("category,-title"), page: 1, pageSize: 3) with { Locale = "en" };

        var first = await client.Client.ListEntriesAsync("article", query, Token);
        var second = await client.Client.ListEntriesAsync("article", query with { Page = 2 }, Token);

        shown.Select(column => column.Name).ShouldBe(Shown);
        query.Fields.ShouldBe(["title", "status", "updatedAt", "updatedBy", "category", "author.name", "tags.name", "scheduledPublishAt", "scheduledUnpublishAt"]);
        query.Populate.ShouldBe(["author", "tags"]);
        first.Total.ShouldBe(4);
        first.Page.ShouldBe(1);
        first.Items.Count.ShouldBe(3);
        second.Items.Count.ShouldBe(1);

        var entries = first.Items.Concat(second.Items).ToList();
        entries.ShouldAllBe(entry => entry.Status != null && entry.UpdatedAt != null && entry.UpdatedBy != null);
        entries.ShouldAllBe(entry => entry.Fields.All(member => member.Key == "title" || member.Key == "category" || member.Key == "author" || member.Key == "tags"));
        entries.Select(entry => entry.Fields["title"]!.GetValue<string>()).Distinct().Count().ShouldBe(4);

        var keys = entries.Select(entry => (Category: entry.Fields["category"]!.GetValue<string>(), Title: entry.Fields["title"]!.GetValue<string>())).ToList();
        keys.ShouldBe([.. keys.OrderBy(key => key.Category, StringComparer.Ordinal).ThenByDescending(key => key.Title, StringComparer.Ordinal)]);

        // A populated relation holds its label and no other field of its target.
        var author = entries.Select(entry => entry.Fields["author"]).OfType<JsonObject>().First();
        author["name"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        author.ContainsKey("bio").ShouldBeFalse();
        author.ContainsKey("slug").ShouldBeFalse();
        TagNames(entries.Select(entry => entry.Fields["tags"]).OfType<JsonArray>().First(tags => tags.Count > 0)).ShouldNotBeNullOrWhiteSpace();
    }

    // What decides the New button: both roles may create articles in the Blog sample.
    [Theory]
    [InlineData(BuiltInRoles.SuperAdminName)]
    [InlineData(BuiltInRoles.EditorName)]
    public async Task TheAccessSaysPerTypeWhetherTheCallerMayCreate(string role)
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(role);

        var access = await client.Client.GetMyAccessAsync(null, null, Token);

        access.Types["article"].Create.ShouldBeTrue();
        access.Types.Keys.ShouldBe(["article", "author", "tag"], ignoreOrder: true);
    }

    private static string? TagNames(JsonNode value) =>
        string.Join(", ", value.AsArray().OfType<JsonObject>().Select(tag => tag["name"]?.GetValue<string>()));
}
