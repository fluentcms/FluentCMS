using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Storage;

public class ContentRenamesTests
{
    private static readonly string[] Hero = ["hero"];

    [Fact]
    public void ALeafTakesTheFormerNamesOfItsFieldAndOfTheComponentsAboveIt()
    {
        var before = SampleSchema.NewBuilder()
            .AddCollection("author", type => type.Field("name", "text"))
            .AddComponent("social", component => component.Field("image", "text").Field("owner", "relation", field => field.Relation("author")))
            .AddComponent("seo", component => component.Field("metaTitle", "text").Field("social", "component", field => field.Setting(FieldSettings.Component, "social")))
            .AddCollection("article", type => type.Localized().Field("caption", "text", field => field.Localizable()).Field("meta", "component", field => field.Setting(FieldSettings.Component, "seo")))
            .Build();
        var after = SampleSchema.NewBuilder()
            .AddCollection("author", type => type.Field("name", "text"))
            .AddComponent("social", component => component.Field("image", "text").Field("person", "relation", field => field.Relation("author").FormerNames("owner")))
            .AddComponent("seo", component => component.Field("pageTitle", "text", field => field.FormerNames("metaTitle")).Field("social", "component", field => field.Setting(FieldSettings.Component, "social")))
            .AddCollection("article", type => type.Localized().Field("heading", "text", field => field.Localizable().FormerNames("caption")).Field("seo", "component", field => field.Setting(FieldSettings.Component, "seo").FormerNames("meta")))
            .Build();

        var renames = Renames(before, after);

        renames.Columns.ShouldBe(
            [
                new ColumnRename("cms_article_i18n", "caption", "heading"),
                new ColumnRename("cms_article", "meta_meta_title", "seo_page_title"),
                new ColumnRename("cms_article", "meta_social_image", "seo_social_image"),
            ],
            ignoreOrder: true);
        renames.Paths.ShouldBe([new PathRename("article", "meta.social.owner", "seo.social.person")]);
        renames.InJson.ShouldBeEmpty();
    }

    // A component is kept in JSON when it is repeated, sits in a block, or sits in another component kept in JSON.
    [Fact]
    public void ARenameInABlockOrAComponentKeptInJsonIsNamedAndOneKeptInColumnsIsNot()
    {
        var before = Held("heading", "street", "line", "metaTitle");
        var after = Held("title", "road", "text", "pageTitle", former: true);

        var renames = Renames(before, after);

        renames.InJson.Select(change => change.ToString()).ShouldBe(
            [
                "field 'block hero.heading' renamed to 'title'",
                "field 'component address.street' renamed to 'road'",
                "field 'component quote.line' renamed to 'text'",
            ],
            ignoreOrder: true);
        renames.Columns.ShouldBe([new ColumnRename("cms_article", "seo_meta_title", "seo_page_title")]);
    }

    [Fact]
    public void ARichTextOrBlocksFieldMovesItsReferencesWithItsColumnAndAPlainColumnOnlyItsColumn()
    {
        var before = SampleSchema.NewBuilder()
            .AddBlock("hero", block => block.Field("heading", "text"))
            .AddCollection("article", type => type.Field("content", "richText").Field("sections", "blocks", field => field.Setting(FieldSettings.Blocks, Hero)).Field("subtitle", "text"))
            .Build();
        var after = SampleSchema.NewBuilder()
            .AddBlock("hero", block => block.Field("heading", "text"))
            .AddCollection("article", type => type
                .Field("body", "richText", field => field.FormerNames("content"))
                .Field("parts", "blocks", field => field.Setting(FieldSettings.Blocks, Hero).FormerNames("sections"))
                .Field("summary", "text", field => field.FormerNames("subtitle")))
            .Build();

        var renames = Renames(before, after);

        renames.Columns.Select(rename => rename.To).ShouldBe(["body", "parts", "summary"], ignoreOrder: true);
        renames.Paths.ShouldBe([new PathRename("article", "content", "body"), new PathRename("article", "sections", "parts")], ignoreOrder: true);
        renames.InJson.ShouldBeEmpty();
    }

    [Fact]
    public void AFieldWithAFormerNameOfAFieldTheSchemaStillHasIsNoRename()
    {
        var schema = SampleSchema.NewBuilder()
            .AddCollection("article", type => type.Field("title", "text").Field("summary", "text", field => field.FormerNames("title")))
            .Build();

        var renames = Renames(ArticleWith("title"), schema);

        renames.Columns.ShouldBeEmpty();
        renames.Paths.ShouldBeEmpty();
    }

    private static ContentRenames Renames(SchemaModel before, SchemaModel after) =>
        ContentRenames.Of(after, SchemaSnapshotDiff.Compare(SchemaSnapshot.Of(before).Json, SchemaSnapshot.Of(after).Json));

    private static SchemaModel ArticleWith(string field) =>
        SampleSchema.NewBuilder().AddCollection("article", type => type.Field(field, "text")).Build();

    private static SchemaModel Held(string heading, string street, string line, string metaTitle, bool former = false)
    {
        Action<IFieldBuilder> Former(string name) => field => field.FormerNames(former ? [name] : []);

        return SampleSchema.NewBuilder()
            .AddComponent("quote", component => component.Field(line, "text", Former("line")))
            .AddBlock("hero", block => block.Field(heading, "text", Former("heading")).Field("quote", "component", field => field.Setting(FieldSettings.Component, "quote")))
            .AddComponent("address", component => component.Field(street, "text", Former("street")))
            .AddComponent("seo", component => component.Field(metaTitle, "text", Former("metaTitle")))
            .AddCollection("article", type => type
                .Field("sections", "blocks", field => field.Setting(FieldSettings.Blocks, Hero))
                .Field("places", "component", field => field.Multiple().Setting(FieldSettings.Component, "address"))
                .Field("seo", "component", field => field.Setting(FieldSettings.Component, "seo")))
            .Build();
    }
}
