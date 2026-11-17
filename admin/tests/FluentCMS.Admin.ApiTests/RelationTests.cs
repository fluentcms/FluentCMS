using System.Text.Json.Nodes;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Lists;
using FluentCMS.Admin.Shell;
using FluentCMS.Core.Authorization;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The reads and the write of the relation picker of A-15 on the Blog sample, as each role the first release is used with:
// the target's list by cursor with its label only, a search of a target that is not searchable as a contains filter on its
// label, the labels of chosen ids, and an ordered relation to many saved and read back in its order.
public sealed class RelationTests
{
    private static CancellationToken Token => AdminHost.Token;

    [Theory]
    [InlineData(BuiltInRoles.SuperAdminName)]
    [InlineData(BuiltInRoles.EditorName)]
    public async Task ThePickerReadsTheTargetsListByCursorAndTheLabelsOfChosenEntries(string role)
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(role);
        var schema = await client.Metadata.GetSchemaAsync(Token);
        var tag = schema.Types.Single(type => type.Name == "tag");
        var article = schema.Types.Single(type => type.Name == "article");

        var query = RelationReads.Page(tag, [], null, searchIndex: false, cursor: null, locale: "en") with { PageSize = 2 };
        var first = await client.Client.ListEntriesAsync("tag", query, Token);
        var second = await client.Client.ListEntriesAsync("tag", query with { Cursor = first.NextCursor.ShouldNotBeNull() }, Token);

        article.Fields.Single(field => field.Name == "tags").Relation.ShouldNotBeNull().Ordered.ShouldBeTrue();
        AdminLabels.LabelField(tag).ShouldBe("name");
        query.Locale.ShouldBeNull();
        query.ToQueryString().ShouldBe("sort=name&pageSize=2&fields=id,status,name,updatedAt&withTotal=false");
        first.Total.ShouldBeNull();
        first.Items.Count.ShouldBe(2);
        second.Items.Count.ShouldBe(1);
        second.NextCursor.ShouldBeNull();
        var names = first.Items.Concat(second.Items).Select(entry => entry.Fields["name"]!.GetValue<string>()).ToList();
        names.ShouldBe(["dotnet", "headless cms", "release notes"]);
        first.Items.Concat(second.Items).ShouldAllBe(entry => entry.Fields.Count == 1 && entry.Status != null);

        RelationReads.FiltersLabel(tag, new ListFilterRegistry()).ShouldBeTrue();
        var found = await client.Client.ListEntriesAsync("tag", RelationReads.Page(tag, [], "less", searchIndex: false, cursor: null, locale: null), Token);
        found.Items.Select(entry => entry.Fields["name"]!.GetValue<string>()).ShouldBe(["headless cms"]);

        var chosen = first.Items.Select(entry => entry.Id).Append(Guid.CreateVersion7()).ToList();
        var labels = await RelationReads.LabelsAsync(client.Client, tag, chosen, null, Token);
        labels.Keys.ShouldBe(chosen.Take(2), ignoreOrder: true);
        labels.Values.Select(entry => entry.Fields["name"]!.GetValue<string>()).ShouldBe(["dotnet", "headless cms"], ignoreOrder: true);
    }

    // The editor writes an id for a relation to one and ids in their order for a relation to many, which the form reads back
    // as it wrote them; both roles may create the targets, so inline create is offered to both.
    [Theory]
    [InlineData(BuiltInRoles.SuperAdminName)]
    [InlineData(BuiltInRoles.EditorName)]
    public async Task AnArticleKeepsItsAuthorAndTheOrderOfItsTags(string role)
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(role);
        var schema = await client.Metadata.GetSchemaAsync(Token);
        var author = schema.Types.Single(type => type.Name == "author");
        var tag = schema.Types.Single(type => type.Name == "tag");
        var access = await client.Client.GetMyAccessAsync(null, null, Token);
        var authors = await client.Client.ListEntriesAsync("author", RelationReads.Page(author, [], null, false, null, null), Token);
        var tags = (await client.Client.ListEntriesAsync("tag", RelationReads.Page(tag, [], null, false, null, null), Token)).Items.Select(entry => entry.Id).ToList();
        var fields = ContentFormLayout.Defaults(schema.Types.Single(type => type.Name == "article"));
        fields["title"] = "With relations";
        fields["author"] = authors.Items[^1].Id.ToString("D");
        fields["tags"] = new JsonArray([.. tags.AsEnumerable().Reverse().Select(id => (JsonNode)id.ToString("D"))]);

        var created = await client.Client.CreateEntryAsync("article", fields, client.Client.StartAction(), Token);
        var read = await client.Client.GetEntryAsync("article", created.Entry!.Id, null, Token);

        (access.Types["author"].Create, access.Types["tag"].Create).ShouldBe((true, true));
        RelationReads.Ids(read.Entry!.Fields["author"]).ShouldBe([authors.Items[^1].Id]);
        RelationReads.Ids(read.Entry.Fields["tags"]).ShouldBe(tags.AsEnumerable().Reverse());

        // A save sends the references back as the read gave them, or ids where the editor changed the relation.
        var changed = read.Entry.Fields.DeepClone().AsObject();
        changed["tags"] = new JsonArray([.. tags.Take(1).Select(id => (JsonNode)id.ToString("D"))]);
        var saved = await client.Client.SaveEntryAsync("article", created.Entry.Id, changed, read, Token);
        RelationReads.Ids(saved.Entry!.Fields["author"]).ShouldBe([authors.Items[^1].Id]);
        RelationReads.Ids(saved.Entry.Fields["tags"]).ShouldBe(tags.Take(1));
    }
}
