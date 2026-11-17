using FluentCMS.Admin.Content;
using FluentCMS.Admin.Lists;
using FluentCMS.Core.Authorization;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The reads the shell starts with, into the admin's own records (AQ6), as each role the first release is used with.
public sealed class ReadTests
{
    private static readonly string[] FirstPartyPlugins = ["ai", "graphql", "mcp", "media", "multi-site", "search", "webhooks"];
    private static readonly string[] NewsAndVideo = ["news", "video"];

    private static CancellationToken Token => AdminHost.Token;

    [Theory]
    [InlineData(BuiltInRoles.SuperAdminName)]
    [InlineData(BuiltInRoles.EditorName)]
    public async Task TheAdminReadsTheCallerTheSitesTheSchemaThePluginsAndAContentList(string role)
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(role);

        var me = await client.Client.GetMeAsync(Token);
        var sites = await client.Client.GetMySitesAsync(Token);
        var schema = await client.Metadata.GetSchemaAsync(Token);
        var plugins = await client.Metadata.GetPluginsAsync(Token);
        var page = await client.Client.ListEntriesAsync("article", new ContentQuery { Sort = [new SortField("title")], PageSize = 2, Locale = "en" }, Token);

        me.Kind.ShouldBe("adminUser");
        me.Roles.Select(assigned => assigned.Name).ShouldContain(role);

        var site = sites.ShouldHaveSingleItem();
        site.IsDefault.ShouldBeTrue();
        site.DefaultLocale.ShouldBe("en");
        site.Locales.Select(locale => locale.Code).ShouldBe(["en", "fr"]);

        schema.Types.Select(type => type.Name).ShouldBe(["article", "author", "tag"], ignoreOrder: true);
        schema.Components.Select(type => type.Name).ShouldBe(["seo"]);
        schema.Blocks.Select(type => type.Name).ShouldBe(["gallery", "hero", "quote"], ignoreOrder: true);
        var article = schema.Types.Single(type => type.Name == "article");
        article.Localized.ShouldBeTrue();
        article.Fields.Single(field => field.Name == "title").Validation.Required.ShouldBeTrue();
        article.Fields.Single(field => field.Name == "author").Relation!.Target.ShouldBe("author");
        article.Fields.Single(field => field.Name == "videoUrl").VisibleWhen!.Field.ShouldBe("category");
        article.Fields.Single(field => field.Name == "category").Settings["options"]!.AsArray().Count.ShouldBe(3);

        plugins.Select(plugin => plugin.Name).ShouldBe(FirstPartyPlugins, ignoreOrder: true);
        plugins.ShouldAllBe(plugin => plugin.Enabled);
        plugins.Single(plugin => plugin.Name == "media").Contributions.FieldTypes.ShouldBe(["media"]);

        page.Items.Count.ShouldBe(2);
        page.Total.ShouldBe(4);
        page.PageSize.ShouldBe(2);
        page.NextCursor.ShouldNotBeNull();
        page.Items.ShouldAllBe(entry => entry.Id != Guid.Empty && entry.Locale == "en" && entry.Version >= 1);
        page.Items.Select(entry => entry.Fields["title"]!.GetValue<string>()).ShouldBeInOrder(SortDirection.Ascending, StringComparer.Ordinal);
        page.Items.ShouldAllBe(entry => !entry.Fields.ContainsKey("id") && !entry.Fields.ContainsKey("version"));
    }

    [Fact]
    public async Task AFilteredListReadsWhatTheQueryWriterAskedFor()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);

        var query = new ContentQuery
        {
            Filter = new And(new FieldFilter("category", FilterOperator.In, NewsAndVideo), new FieldFilter("sections", FilterOperator.HasBlock, "hero")),
            Fields = ["title", "category"],
        };
        var page = await client.Client.ListEntriesAsync("article", query, Token);

        page.Items.ShouldNotBeEmpty();
        page.Items.ShouldAllBe(entry => entry.Fields["category"]!.GetValue<string>() == "news" || entry.Fields["category"]!.GetValue<string>() == "video");
        page.Items.ShouldAllBe(entry => !entry.Fields.ContainsKey("body"));
    }
}
