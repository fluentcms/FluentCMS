using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema;

// The display name and the title field of a content type (#3150): set by the builder, the attributes and schema.json,
// carried by the snapshot and its diff, and refused at build when the title field is not a single text field.
public sealed class TypeLabelTests
{
    private static readonly string[] Options = ["a", "b"];

    private const string Labelled = """
        {
          "collections": [
            {
              "name": "blog-post",
              "displayName": "Blog post",
              "titleField": "headline",
              "fields": [ { "name": "headline", "type": "text" }, { "name": "body", "type": "richText" } ]
            }
          ],
          "singletons": [ { "name": "site-settings", "displayName": "Settings", "fields": [ { "name": "siteName", "type": "text" } ] } ]
        }
        """;

    private static SchemaValidationException Fails(SchemaBuilder builder) =>
        Should.Throw<SchemaValidationException>(() => builder.Build());

    [Fact]
    public void TheBuilderSetsBoth()
    {
        var type = new SchemaBuilder()
            .AddCollection("blog-post", type => type.DisplayName("Blog post").TitleField("headline").Field("headline", "text"))
            .Build()
            .FindContentType("blog-post")!;

        type.DisplayName.ShouldBe("Blog post");
        type.TitleField.ShouldBe("headline");
    }

    [Fact]
    public void ATypeWithoutThemHasNull()
    {
        var model = new SchemaBuilder()
            .AddCollection("article", type => type.Field("title", "text"))
            .AddComponent("seo", type => type.Field("title", "text"))
            .Build();

        model.FindContentType("article")!.DisplayName.ShouldBeNull();
        model.FindContentType("article")!.TitleField.ShouldBeNull();
        var described = SchemaDescriber.Describe(model);
        described.Types.ShouldHaveSingleItem().DisplayName.ShouldBeNull();
        described.Types.ShouldHaveSingleItem().TitleField.ShouldBeNull();
        described.Components.ShouldHaveSingleItem().DisplayName.ShouldBeNull();
    }

    [Fact]
    public void TheBuilderRefusesThemOnAComponentAndABlock()
    {
        Should.Throw<ArgumentException>(() => new SchemaBuilder().AddComponent("seo", type => type.DisplayName("SEO")).Build());
        Should.Throw<ArgumentException>(() => new SchemaBuilder().AddBlock("hero", type => type.TitleField("heading").Field("heading", "text")).Build());
    }

    [FluentCMS.Schema.Collection("blog-post", DisplayName = "Blog post", TitleField = nameof(Headline))]
    private sealed class BlogPost : ContentItem
    {
        [Text]
        public string? Headline { get; set; }
    }

    [Singleton("site-settings", DisplayName = "Settings", TitleField = nameof(SiteName))]
    private sealed class SiteSettings : ContentItem
    {
        [Text]
        public string? SiteName { get; set; }
    }

    [FluentCMS.Schema.Collection("untitled", TitleField = "Missing")]
    private sealed class Untitled : ContentItem
    {
        [Text]
        public string? Name { get; set; }
    }

    [Fact]
    public void TheAttributesSetBothWithTheTitleFieldInCamelCase()
    {
        var model = new SchemaBuilder().AddContentType<BlogPost>().AddContentType<SiteSettings>().Build();

        model.FindContentType("blog-post")!.DisplayName.ShouldBe("Blog post");
        model.FindContentType("blog-post")!.TitleField.ShouldBe("headline");
        model.FindContentType("site-settings")!.DisplayName.ShouldBe("Settings");
        model.FindContentType("site-settings")!.TitleField.ShouldBe("siteName");
        Fails(new SchemaBuilder().AddContentType<Untitled>()).Errors.Select(error => error.Code).ShouldBe(["FCMS1220"]);
    }

    [Fact]
    public void SchemaJsonSetsBothAndTheSnapshotRoundTrips()
    {
        var model = new SchemaBuilder().AddSchemaJson(Labelled).Build();
        var json = SchemaJsonWriter.Write(model);
        var again = new SchemaBuilder().AddSchemaJson(json).Build();

        again.FindContentType("blog-post")!.DisplayName.ShouldBe("Blog post");
        again.FindContentType("blog-post")!.TitleField.ShouldBe("headline");
        again.FindContentType("site-settings")!.DisplayName.ShouldBe("Settings");
        again.FindContentType("site-settings")!.TitleField.ShouldBeNull();
        SchemaJsonWriter.Write(again).ShouldBe(json);
        json.ShouldContain("\"displayName\": \"Blog post\",\n      \"titleField\": \"headline\",\n      \"fields\"");
    }

    [Fact]
    public void NeitherIsWrittenWhenNotSetSoNoExistingSnapshotChanges()
    {
        var json = SchemaJsonWriter.Write(new SchemaBuilder().AddCollection("article", type => type.Field("title", "text")).Build());

        json.ShouldNotContain("displayName");
        json.ShouldNotContain("titleField");
    }

    [Theory]
    [InlineData("""{ "components": [ { "name": "seo", "displayName": "SEO" } ] }""")]
    [InlineData("""{ "blocks": [ { "name": "hero", "titleField": "heading", "fields": [ { "name": "heading", "type": "text" } ] } ] }""")]
    [InlineData("""{ "collections": [ { "name": "article", "displayName": 5 } ] }""")]
    [InlineData("""{ "collections": [ { "name": "article", "titleField": null } ] }""")]
    public void SchemaJsonRefusesThemOnAPartAndAsOtherThanText(string json) =>
        Fails(new SchemaBuilder().AddSchemaJson(json)).Errors.Select(error => error.Code).ShouldBe(["FCMS1213"]);

    [Fact]
    public void ATitleFieldThatIsNotAFieldOfTheTypeIsFCMS1220()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type.TitleField("title").Field("name", "text"));

        Fails(builder).Errors.ShouldHaveSingleItem().ToString().ShouldBe(
            "FCMS1220: title field 'title' of collection 'article' is not a field of it; a title field must be a single text field of the type");
    }

    [Theory]
    [InlineData("summary", "a longText field")]
    [InlineData("slug", "a slug field")]
    [InlineData("tags", "a multiple select field")]
    [InlineData("Name", "not a field of it")]
    public void ATitleFieldThatIsNotASingleTextFieldIsFCMS1220(string titleField, string message)
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .TitleField(titleField)
            .Field("name", "text")
            .Field("summary", "longText")
            .Field("slug", "slug", field => field.Setting(FieldSettings.Source, "name"))
            .Field("tags", "select", field => field.Multiple().Setting(FieldSettings.Options, Options)));

        var error = Fails(builder).Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe("FCMS1220");
        error.Message.ShouldContain(message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankDisplayNameIsFCMS1215(string displayName)
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type.DisplayName(displayName).Field("title", "text"));

        var error = Fails(builder).Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe("FCMS1215");
        error.Message.ShouldContain("blank display name");
    }

    [Fact]
    public void TheDescriptionCarriesBoth()
    {
        var described = SchemaDescriber.Describe(new SchemaBuilder().AddSchemaJson(Labelled).Build());

        var post = described.Types.Single(type => type.Name == "blog-post");
        post.DisplayName.ShouldBe("Blog post");
        post.TitleField.ShouldBe("headline");
        described.Types.Single(type => type.Name == "site-settings").TitleField.ShouldBeNull();
    }

    [Fact]
    public void TheDiffReportsAChangeOfEitherAsAChangeOfTheTypeThatLosesNothing()
    {
        var before = new SchemaBuilder().AddCollection("article", type => type.Field("title", "text").Field("name", "text")).Build();
        var after = new SchemaBuilder().AddCollection("article", type => type.DisplayName("Article").TitleField("title").Field("title", "text").Field("name", "text")).Build();
        var renamed = new SchemaBuilder().AddCollection("article", type => type.DisplayName("Story").TitleField("name").Field("title", "text").Field("name", "text")).Build();

        var report = SchemaSnapshotDiff.Report(SchemaSnapshot.Of(before).Json, SchemaSnapshot.Of(after).Json);
        report.Changes.Select(change => change.ToString()).ShouldBe(["type 'article' changed (displayName, titleField)"]);
        report.Destructive.ShouldBeEmpty();
        SchemaSnapshotDiff.Compare(SchemaSnapshot.Of(after).Json, SchemaSnapshot.Of(renamed).Json).Select(change => change.ToString())
            .ShouldBe(["type 'article' changed (displayName, titleField)"]);
        SchemaSnapshot.Of(before).Hash.ShouldNotBe(SchemaSnapshot.Of(after).Hash);
    }
}
