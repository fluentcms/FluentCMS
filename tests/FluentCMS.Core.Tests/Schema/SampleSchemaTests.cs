using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema;

public class SampleSchemaTests
{
    [Fact]
    public void SampleTypesBuildIntoModel()
    {
        var model = SampleSchema.FromClasses().Build();

        model.ContentTypes.Select(type => type.Name).ShouldBe(["article", "author", "site-settings", "tag"]);
        model.Components.Select(type => type.Name).ShouldBe(["seo"]);
        model.Blocks.Select(type => type.Name).ShouldBe(["gallery", "hero", "quote"]);

        model.FindContentType("article")!.Kind.ShouldBe(SchemaTypeKind.Collection);
        model.FindContentType("site-settings")!.Kind.ShouldBe(SchemaTypeKind.Singleton);
        model.FindContentType(typeof(Article)).ShouldBeSameAs(model.FindContentType("article"));
        model.FindComponent("seo")!.Kind.ShouldBe(SchemaTypeKind.Component);
        model.FindBlock("hero")!.Kind.ShouldBe(SchemaTypeKind.Block);
    }

    [Fact]
    public void ArticleFieldsKeepTheirDeclarationOrder()
    {
        var article = SampleSchema.FromClasses().Build().FindContentType("article")!;

        article.Localized.ShouldBeTrue();
        article.Fields.Select(field => field.Name).ShouldBe(
            ["title", "slug", "body", "author", "tags", "hero", "sections", "seo", "category", "publishedOn", "videoUrl"]);
        article.Fields.Select(field => field.FieldType).ShouldBe(
            ["text", "slug", "richText", "relation", "relation", "media", "blocks", "component", "select", "dateTime", "url"]);
    }

    [Fact]
    public void ArticleFieldsCarryTheirModifiers()
    {
        var article = SampleSchema.FromClasses().Build().FindContentType("article")!;

        var title = article.FindField("title")!;
        title.MaxLength.ShouldBe(200);
        title.Required.ShouldBeTrue();
        title.Localizable.ShouldBeTrue();
        title.Searchable.ShouldBeTrue();

        var slug = article.FindField("slug")!;
        slug.Settings[FieldSettings.Source].ShouldBe("title");
        slug.Unique.ShouldBeTrue();

        var category = article.FindField("category")!;
        category.Indexed.ShouldBeTrue();
        Strings(category.Settings[FieldSettings.Options]).ShouldBe(["news", "opinion", "video"]);

        article.FindField("publishedOn")!.Indexed.ShouldBeTrue();
        article.FindField("hero")!.Multiple.ShouldBeFalse();
        article.FindField("seo")!.Settings[FieldSettings.Component].ShouldBe("seo");
        Strings(article.FindField("sections")!.Settings[FieldSettings.Blocks]).ShouldBe(["hero", "quote", "gallery"]);
    }

    [Fact]
    public void RelationsCarryTargetOrderAndReverseSide()
    {
        var model = SampleSchema.FromClasses().Build();
        var article = model.FindContentType("article")!;

        var author = article.FindField("author")!;
        author.Relation!.Target.ShouldBe("author");
        author.Relation.Reverse.ShouldBe("articles");
        author.Multiple.ShouldBeFalse();

        var tags = article.FindField("tags")!;
        tags.Relation!.Target.ShouldBe("tag");
        tags.Relation.Ordered.ShouldBeTrue();
        tags.Relation.Reverse.ShouldBeNull();
        tags.Multiple.ShouldBeTrue();

        var reverse = model.FindContentType("author")!.ReverseRelations.ShouldHaveSingleItem();
        reverse.Name.ShouldBe("articles");
        reverse.SourceType.ShouldBe("article");
        reverse.SourceField.ShouldBe("author");
        model.FindContentType("tag")!.ReverseRelations.ShouldBeEmpty();
    }

    [Fact]
    public void ConditionsAreDataOnTheField()
    {
        var videoUrl = SampleSchema.FromClasses().Build().FindContentType("article")!.FindField("videoUrl")!;

        var visibleWhen = videoUrl.VisibleWhen.ShouldNotBeNull();
        visibleWhen.Operator.ShouldBe(ConditionOperator.Equal);
        visibleWhen.FieldName.ShouldBe("category");
        visibleWhen.Values.ShouldBe(["video"]);
        videoUrl.RequiredWhen.ShouldNotBeNull().ToString().ShouldBe("category == \"video\"");
    }

    [Fact]
    public void BlocksAndComponentsHaveTheirFields()
    {
        var model = SampleSchema.FromClasses().Build();

        model.FindBlock("hero")!.Fields.Select(field => field.Name).ShouldBe(["heading", "image"]);
        model.FindBlock("gallery")!.FindField("images")!.Multiple.ShouldBeTrue();
        model.FindComponent("seo")!.FindField("metaTitle")!.MaxLength.ShouldBe(70);
        model.FindContentType("site-settings")!.Fields.Select(field => field.Name).ShouldBe(["siteName", "description", "logo"]);
    }

    [Fact]
    public void SchemaFileProducesTheSameModelAsTheClasses()
    {
        var fromClasses = SampleSchema.Canonical(SampleSchema.FromClasses());
        var fromFile = SampleSchema.Canonical(SampleSchema.FromFile());

        fromFile.ShouldBe(fromClasses);
    }

    [Fact]
    public void SchemaJsonTextProducesTheSameModelAsTheFile()
    {
        var text = File.ReadAllText(SampleSchema.FilePath);

        var fromText = SampleSchema.Canonical(SampleSchema.NewBuilder().AddSchemaJson(text));

        fromText.ShouldBe(SampleSchema.Canonical(SampleSchema.FromFile()));
    }

    [Fact]
    public void ModelFromFileHasReverseSidesToo()
    {
        var model = SampleSchema.FromFile().Build();

        model.FindContentType("author")!.ReverseRelations.ShouldHaveSingleItem().Name.ShouldBe("articles");
        model.FindContentType("article")!.ClrType.ShouldBeNull();
    }

    [Fact]
    public void CanonicalJsonReadsBackIntoTheSameModel()
    {
        var canonical = SampleSchema.Canonical(SampleSchema.FromClasses());

        SampleSchema.Canonical(SampleSchema.NewBuilder().AddSchemaJson(canonical)).ShouldBe(canonical);
    }

    [Fact]
    public void AssemblyScanFindsTheSampleTypes()
    {
        var scanned = SampleSchema.Canonical(SampleSchema.NewBuilder().AddContentTypesFromAssembly(SampleSchema.SamplesOnly()));

        scanned.ShouldBe(SampleSchema.Canonical(SampleSchema.FromClasses()));
    }

    [Fact]
    public void TypesAreOrderedByNameWhateverTheOrderTheyWereAdded()
    {
        var reversed = SampleSchema.NewBuilder()
            .AddContentType<SiteSettings>()
            .AddContentType<Tag>()
            .AddContentType<Author>()
            .AddContentType<Article>();

        SampleSchema.Canonical(reversed).ShouldBe(SampleSchema.Canonical(SampleSchema.FromClasses()));
    }

    private static IEnumerable<string> Strings(object value) => (IEnumerable<string>)value;
}
