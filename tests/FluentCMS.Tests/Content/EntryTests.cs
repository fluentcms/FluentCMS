using FluentCMS.Core.Content;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using static FluentCMS.Tests.Content.TestEntries;

namespace FluentCMS.Tests.Content;

public sealed class EntryTests
{
    [Fact]
    public void A_created_entry_starts_at_version_one_unpublished_in_its_types_site()
    {
        var type = Type(Page);

        var entry = Entry.Create(type, Json("""{"headline": "Hello", "body": null}"""), Registry, Now).Value;

        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.Equal(Site.Default.Id, entry.SiteId);
        Assert.Equal(type.Id, entry.ContentTypeId);
        Assert.Equal("page", entry.ContentType);
        Assert.Equal(1, entry.LatestVersion);
        Assert.Null(entry.PublishedVersion);
        Assert.Equal(EntryState.Draft, entry.State);
        Assert.Equal(entry.Id, entry.Latest.EntryId);
        Assert.Equal(1, entry.Latest.ContentTypeVersion);
        Assert.Equal(Now, entry.Latest.CreatedAt);
        Assert.Equal("""{"headline":"Hello"}""", entry.Latest.Fields.GetRawText());
    }

    [Fact]
    public void Invalid_fields_create_nothing()
    {
        var created = Entry.Create(Type(Page), Json("{}"), Registry, Now);

        Assert.False(created.IsSuccess);
        Assert.Equal(ContentErrorCodes.FieldRequired, Assert.Single(created.Errors).Code);
    }

    [Fact]
    public void A_revision_is_the_next_version_checked_against_the_type_version_given()
    {
        var type = Type(Page);
        var entry = Entry.Create(type, Json("""{"headline": "Hello"}"""), Registry, Now).Value;
        var revisedType = type.Revise(new ContentTypeDefinition([.. Page.Fields, Field("summary", "text", required: true)]), Registry).Value;

        var missingSummary = entry.Revise(revisedType, Json("""{"headline": "Hello again"}"""), Registry, Now.AddMinutes(1));
        var revised = entry.Revise(revisedType, Json("""{"headline": "Hello again", "summary": "Short"}"""), Registry, Now.AddMinutes(1)).Value;

        Assert.Equal(ContentErrorCodes.FieldRequired, Assert.Single(missingSummary.Errors).Code);
        Assert.Equal(entry.Id, revised.Id);
        Assert.Equal(2, revised.LatestVersion);
        Assert.Equal(2, revised.Latest.ContentTypeVersion);
        Assert.Equal(Now.AddMinutes(1), revised.Latest.CreatedAt);
        Assert.Equal("""{"headline":"Hello again","summary":"Short"}""", revised.Latest.Fields.GetRawText());

        // The entry it was revised from is left as it was.
        Assert.Equal(1, entry.LatestVersion);
        Assert.Equal("""{"headline":"Hello"}""", entry.Latest.Fields.GetRawText());
    }

    [Fact]
    public void A_revision_keeps_the_published_version_and_the_state()
    {
        var type = Type(Page);
        var id = Guid.CreateVersion7();
        var stored = Entry.Restore(
            id,
            Site.Default.Id,
            type.Id,
            type.Slug,
            publishedVersion: 3,
            EntryState.Published,
            new EntryVersion(id, 4, 1, Json("""{"headline": "Four"}"""), Now));

        var revised = stored.Revise(type, Json("""{"headline": "Five"}"""), Registry, Now).Value;

        Assert.Equal(5, revised.LatestVersion);
        Assert.Equal(3, revised.PublishedVersion);
        Assert.Equal(EntryState.Published, revised.State);
    }

    [Fact]
    public void A_restored_entry_holds_one_of_its_own_versions()
    {
        var type = Type(Page);
        var version = new EntryVersion(Guid.CreateVersion7(), 1, 1, Json("{}"), Now);

        Assert.Throws<ArgumentException>(() => Entry.Restore(Guid.CreateVersion7(), Site.Default.Id, type.Id, type.Slug, null, EntryState.Draft, version));
    }

    [Fact]
    public void A_revision_against_another_content_type_is_a_programming_error()
    {
        var entry = Entry.Create(Type(Page), Json("""{"headline": "Hello"}"""), Registry, Now).Value;

        Assert.Throws<ArgumentException>(() => entry.Revise(Type(Page, "other-page"), Json("""{"headline": "Hello"}"""), Registry, Now));
    }

    [Fact]
    public void A_version_reads_back_its_own_copy_of_the_fields()
    {
        var version = new EntryVersion(Guid.CreateVersion7(), 1, 1, Json("""{"headline": "Hello"}"""), Now);

        Assert.Equal("""{"headline": "Hello"}""", version.Fields.GetRawText());
        Assert.Throws<ArgumentException>(() => new EntryVersion(Guid.CreateVersion7(), 1, 1, Json("[]"), Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntryVersion(Guid.CreateVersion7(), 0, 1, Json("{}"), Now));
    }
}
