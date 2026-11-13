using FluentCMS.Content;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// A version written before a field was renamed with [FormerNames] holds the field under its former name, and a read, a
// diff and a restore find it there (#3085).
public sealed class RenamedFieldVersionTests : IDisposable
{
    private static readonly SchemaModel Model = SampleSchema.NewBuilder()
        .AddComponent("seo", component => component
            .Field("pageTitle", "text", field => field.FormerNames("metaTitle"))
            .Field("noIndex", "boolean"))
        .AddCollection("post", type => type
            .Localized()
            .Field("title", "text", field => field.Required().Localizable().FormerNames("heading"))
            .Field("summary", "text", field => field.FormerNames("teaser", "subtitle"))
            .Field("seo", "component", field => field.Setting(FieldSettings.Component, "seo").FormerNames("meta"))
            .Field("views", "integer"))
        .Build();

    private readonly ServiceDatabase _database = new(schema: Model);

    public void Dispose() => _database.Dispose();

    private static ContentDocument Post(string title, string summary, string pageTitle, int views) => new()
    {
        ["title"] = title,
        ["summary"] = summary,
        ["seo"] = new ContentDocument { ["pageTitle"] = pageTitle, ["noIndex"] = false },
        ["views"] = views,
    };

    // Version 1 as a build before the renames wrote it, and version 2 after them. Returns version 1 as it was written.
    private async Task<(Guid Id, string Original)> RenamedAsync()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Post("One", "First", "Meta one", 1), "post"));
        await _database.UpdateAsync(Editor, id, Post("Two", "Second", "Meta two", 1), 1, "post");
        var original = _database.Versions(id)[0].Snapshot;

        _database.Read(context =>
        {
            var version = context.Versions.Single(version => version.DocumentId == id && version.Number == 1);
            version.Snapshot = version.Snapshot
                .Replace("\"pageTitle\":", "\"metaTitle\":", StringComparison.Ordinal)
                .Replace("\"seo\":", "\"meta\":", StringComparison.Ordinal)
                .Replace("\"title\":", "\"heading\":", StringComparison.Ordinal)
                .Replace("\"summary\":", "\"subtitle\":", StringComparison.Ordinal);
            return context.SaveChanges();
        });

        return (id, original);
    }

    [Fact]
    public async Task RestoringAVersionFromBeforeARenameRestoresTheRenamedFields()
    {
        var (id, original) = await RenamedAsync();

        var restored = await _database.RestoreVersionAsync(Editor, id, 1, type: "post");

        restored["title"].ShouldBe("One");
        restored["summary"].ShouldBe("First");
        restored["seo"].ShouldBeOfType<ContentDocument>()["pageTitle"].ShouldBe("Meta one");
        _database.Row("cms_post_i18n", id, "en")["title"].ShouldBe("One");
        _database.Versions(id)[^1].Snapshot.ShouldBe(original);
    }

    [Fact]
    public async Task AVersionFromBeforeARenameReadsUnderTheNewNames()
    {
        var (id, _) = await RenamedAsync();

        var version = (await _database.VersionAsync(Editor, id, 1, type: "post")).ShouldNotBeNull();

        version["title"].ShouldBe("One");
        version["summary"].ShouldBe("First");
        version["seo"].ShouldBeOfType<ContentDocument>()["pageTitle"].ShouldBe("Meta one");
        version.Keys.ShouldNotContain("heading");
        version.Keys.ShouldNotContain("subtitle");
        version.Keys.ShouldNotContain("meta");
    }

    [Fact]
    public async Task ADiffAcrossARenameNamesTheFieldsThatChangedUnderTheirNewNames()
    {
        var (id, _) = await RenamedAsync();

        var changes = await _database.DiffAsync(Editor, id, 1, 2, type: "post");

        changes.Select(change => (change.Path, change.Kind)).ShouldBe(
            [("title", VersionChangeKind.Changed), ("summary", VersionChangeKind.Changed), ("seo.pageTitle", VersionChangeKind.Changed)],
            ignoreOrder: true);
    }
}
