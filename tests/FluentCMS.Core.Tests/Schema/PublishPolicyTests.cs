using FluentCMS.Core.Schema;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema;

public sealed class PublishPolicyTests
{
    [Fact]
    public void JsonRoundTrips()
    {
        var model = new SchemaBuilder().AddSchemaJson("""{ "collections": [ { "name": "article", "unpublishedReferences": "block", "maxVersions": 7, "trashRetentionDays": 9 } ] }""").Build();

        var type = model.FindContentType("article")!;
        type.UnpublishedReferences.ShouldBe(UnpublishedReferencePolicy.Block);
        type.MaxVersions.ShouldBe(7);
        type.TrashRetentionDays.ShouldBe(9);

        var again = new SchemaBuilder().AddSchemaJson(SchemaJsonWriter.Write(model)).Build().FindContentType("article")!;
        again.UnpublishedReferences.ShouldBe(UnpublishedReferencePolicy.Block);
        again.MaxVersions.ShouldBe(7);
        again.TrashRetentionDays.ShouldBe(9);
    }

    [Fact]
    public void TheDefaultsAreNotWrittenSoNoExistingModelChangesItsKey()
    {
        var json = SchemaJsonWriter.Write(new SchemaBuilder().AddCollection("article", type => type.PublishPolicy()).Build());

        json.ShouldNotContain("unpublishedReferences");
        json.ShouldNotContain("maxVersions");
        json.ShouldNotContain("trashRetentionDays");
        new SchemaBuilder().AddCollection("article", type => type.PublishPolicy()).Build().FindContentType("article")!.TrashRetentionDays.ShouldBe(PublishPolicyAttribute.DefaultTrashRetentionDays);
    }

    [Theory]
    [InlineData("""{ "collections": [ { "name": "article", "unpublishedReferences": "ignore" } ] }""")]
    [InlineData("""{ "collections": [ { "name": "article", "maxVersions": 0 } ] }""")]
    [InlineData("""{ "collections": [ { "name": "article", "trashRetentionDays": 0 } ] }""")]
    [InlineData("""{ "components": [ { "name": "seo", "trashRetentionDays": 5 } ] }""")]
    [InlineData("""{ "components": [ { "name": "seo", "maxVersions": 5 } ] }""")]
    [InlineData("""{ "blocks": [ { "name": "hero", "unpublishedReferences": "block" } ] }""")]
    public void JsonRefusesWhatIsNotAPolicy(string json) =>
        Should.Throw<SchemaValidationException>(() => new SchemaBuilder().AddSchemaJson(json).Build());

    [Fact]
    public void TheBuilderRefusesAPolicyOnAComponentAndACapBelowOne()
    {
        Should.Throw<ArgumentException>(() => new SchemaBuilder().AddComponent("seo", type => type.PublishPolicy()).Build());
        Should.Throw<ArgumentOutOfRangeException>(() => new SchemaBuilder().AddCollection("article", type => type.PublishPolicy(maxVersions: 0)).Build());
        Should.Throw<ArgumentOutOfRangeException>(() => new SchemaBuilder().AddCollection("article", type => type.PublishPolicy(trashRetentionDays: 0)).Build());
    }

    [FluentCMS.Schema.Collection("capless")]
    [PublishPolicy(MaxVersions = 0)]
    private sealed class Capless : ContentItem
    {
    }

    [FluentCMS.Schema.Collection("hoarder")]
    [PublishPolicy(TrashRetentionDays = 0)]
    private sealed class Hoarder : ContentItem
    {
    }

    [FluentCMS.Schema.Collection("keeper")]
    [PublishPolicy(TrashRetentionDays = 14)]
    private sealed class Keeper : ContentItem
    {
    }

    [Component("policed")]
    [PublishPolicy]
    private sealed class Policed
    {
    }

    [Fact]
    public void AttributesReportWhatTheBuilderRefuses()
    {
        Should.Throw<SchemaValidationException>(() => new SchemaBuilder().AddContentType<Capless>().Build());
        Should.Throw<SchemaValidationException>(() => new SchemaBuilder().AddContentType<Hoarder>().Build());
        new SchemaBuilder().AddContentType<Keeper>().Build().FindContentType("keeper")!.TrashRetentionDays.ShouldBe(14);
        Should.Throw<SchemaValidationException>(() => new SchemaBuilder().AddContentType<Policed>().Build());
    }
}
