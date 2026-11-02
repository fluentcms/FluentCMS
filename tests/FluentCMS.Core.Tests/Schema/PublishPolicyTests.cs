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
        var model = new SchemaBuilder().AddSchemaJson("""{ "collections": [ { "name": "article", "unpublishedReferences": "block", "maxVersions": 7 } ] }""").Build();

        var type = model.FindContentType("article")!;
        type.UnpublishedReferences.ShouldBe(UnpublishedReferencePolicy.Block);
        type.MaxVersions.ShouldBe(7);

        var again = new SchemaBuilder().AddSchemaJson(SchemaJsonWriter.Write(model)).Build().FindContentType("article")!;
        again.UnpublishedReferences.ShouldBe(UnpublishedReferencePolicy.Block);
        again.MaxVersions.ShouldBe(7);
    }

    [Fact]
    public void TheDefaultsAreNotWrittenSoNoExistingModelChangesItsKey()
    {
        var json = SchemaJsonWriter.Write(new SchemaBuilder().AddCollection("article", type => type.PublishPolicy()).Build());

        json.ShouldNotContain("unpublishedReferences");
        json.ShouldNotContain("maxVersions");
    }

    [Theory]
    [InlineData("""{ "collections": [ { "name": "article", "unpublishedReferences": "ignore" } ] }""")]
    [InlineData("""{ "collections": [ { "name": "article", "maxVersions": 0 } ] }""")]
    [InlineData("""{ "components": [ { "name": "seo", "maxVersions": 5 } ] }""")]
    [InlineData("""{ "blocks": [ { "name": "hero", "unpublishedReferences": "block" } ] }""")]
    public void JsonRefusesWhatIsNotAPolicy(string json) =>
        Should.Throw<SchemaValidationException>(() => new SchemaBuilder().AddSchemaJson(json).Build());

    [Fact]
    public void TheBuilderRefusesAPolicyOnAComponentAndACapBelowOne()
    {
        Should.Throw<ArgumentException>(() => new SchemaBuilder().AddComponent("seo", type => type.PublishPolicy()).Build());
        Should.Throw<ArgumentOutOfRangeException>(() => new SchemaBuilder().AddCollection("article", type => type.PublishPolicy(maxVersions: 0)).Build());
    }

    [FluentCMS.Schema.Collection("capless")]
    [PublishPolicy(MaxVersions = 0)]
    private sealed class Capless : ContentItem
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
        Should.Throw<SchemaValidationException>(() => new SchemaBuilder().AddContentType<Policed>().Build());
    }
}
