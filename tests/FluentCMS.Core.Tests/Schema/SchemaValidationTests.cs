using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema;

public class SchemaValidationTests
{
    private static SchemaValidationException Fails(SchemaBuilder builder) =>
        Should.Throw<SchemaValidationException>(() => builder.Build());

    private static void ShouldReportOnly(SchemaBuilder builder, string code, string messagePart)
    {
        var errors = Fails(builder).Errors;

        errors.Select(error => error.Code).ShouldBe([code]);
        errors[0].Message.ShouldContain(messagePart);
    }

    private static string[] Names(params string[] names) => names;

    private static SchemaBuilder WithAuthor(SchemaBuilder builder) =>
        builder.AddCollection("author", type => type.Field("name", "text"));

    [Fact]
    public void ValidSchemaBuilds()
    {
        var model = WithAuthor(new SchemaBuilder())
            .AddCollection("article", type => type
                .Localized()
                .Field("title", "text", field => field.Localizable().Required())
                .Field("slug", "slug", field => field.Setting(FieldSettings.Source, "title"))
                .Field("author", "relation", field => field.Relation("author", "articles", false)))
            .Build();

        model.ContentTypes.Select(type => type.Name).ShouldBe(["article", "author"]);
    }

    [Fact]
    public void DuplicateTypeNameIsFCMS1201()
    {
        var builder = new SchemaBuilder()
            .AddCollection("article", type => type.Field("title", "text"))
            .AddCollection("article", type => type.Field("body", "text"));

        ShouldReportOnly(builder, "FCMS1201", "collection 'article' is defined more than once");
    }

    [Fact]
    public void SameNameInDifferentKindsIsNotADuplicate()
    {
        var model = new SchemaBuilder()
            .AddCollection("quote", type => type.Field("text", "text"))
            .AddBlock("quote", type => type.Field("text", "text"))
            .Build();

        model.FindContentType("quote").ShouldNotBeNull();
        model.FindBlock("quote").ShouldNotBeNull();
    }

    [Fact]
    public void DuplicateFieldNameIsFCMS1202()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("title", "text")
            .Field("title", "longText"));

        ShouldReportOnly(builder, "FCMS1202", "field 'title' is declared more than once in collection 'article'");
    }

    [Fact]
    public void FieldTypeThatIsNotRegisteredIsFCMS1203()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type.Field("hero", "media"));

        var error = Fails(builder).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe("FCMS1203");
        error.ToString().ShouldBe("FCMS1203: field type 'media' used by article.hero is not registered");
    }

    [Fact]
    public void RelationTargetThatDoesNotExistIsFCMS1204()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("author", "relation", field => field.Relation("author")));

        ShouldReportOnly(builder, "FCMS1204", "relation target 'author' of article.author");
    }

    [Fact]
    public void RelationTargetThatIsAComponentIsFCMS1204()
    {
        var builder = new SchemaBuilder()
            .AddComponent("seo", type => type.Field("metaTitle", "text"))
            .AddCollection("article", type => type.Field("seo", "relation", field => field.Relation("seo")));

        ShouldReportOnly(builder, "FCMS1204", "relation target 'seo'");
    }

    [Fact]
    public void SlugSourceThatDoesNotExistIsFCMS1205()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("slug", "slug", field => field.Setting(FieldSettings.Source, "title")));

        ShouldReportOnly(builder, "FCMS1205", "slug source 'title' of article.slug");
    }

    [Fact]
    public void SlugSourceThatIsTheSlugItselfIsFCMS1205()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("slug", "slug", field => field.Setting(FieldSettings.Source, "slug")));

        ShouldReportOnly(builder, "FCMS1205", "slug source 'slug'");
    }

    [Theory]
    [InlineData("id")]
    [InlineData("createdAt")]
    [InlineData("publishedAt")]
    [InlineData("status")]
    [InlineData("scheduledPublishAt")]
    [InlineData("scheduledUnpublishAt")]
    [InlineData("deletedAt")]
    [InlineData("deletedBy")]
    public void ReservedFieldNameIsFCMS1206(string name)
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type.Field(name, "text"));

        ShouldReportOnly(builder, "FCMS1206", $"field name '{name}' of article.{name} is reserved");
    }

    [Fact]
    public void BlockFieldCalledTypeIsReservedForTheDiscriminatorFCMS1206()
    {
        var builder = new SchemaBuilder().AddBlock("hero", type => type.Field("type", "text"));

        ShouldReportOnly(builder, "FCMS1206", "discriminator");
    }

    [Fact]
    public void BlockFieldCalledIdIsReservedForTheBlockIdFCMS1206()
    {
        var builder = new SchemaBuilder().AddBlock("hero", type => type.Field("id", "text"));

        ShouldReportOnly(builder, "FCMS1206", "block id");
    }

    [Fact]
    public void ReservedNamesAreOnlyReservedOnContentTypes()
    {
        var model = new SchemaBuilder().AddComponent("seo", type => type.Field("status", "text")).Build();

        model.FindComponent("seo")!.FindField("status").ShouldNotBeNull();
    }

    [Fact]
    public void NestingOfThreeLevelsIsAllowed()
    {
        var model = new SchemaBuilder()
            .AddComponent("level-one", type => type.Field("next", "component", field => field.Setting(FieldSettings.Component, "level-two")))
            .AddComponent("level-two", type => type.Field("next", "component", field => field.Setting(FieldSettings.Component, "level-three")))
            .AddComponent("level-three", type => type.Field("title", "text"))
            .Build();

        model.Components.Count.ShouldBe(3);
    }

    [Fact]
    public void NestingOfFourLevelsIsFCMS1207()
    {
        var builder = new SchemaBuilder()
            .AddComponent("level-one", type => type.Field("next", "component", field => field.Setting(FieldSettings.Component, "level-two")))
            .AddComponent("level-two", type => type.Field("next", "component", field => field.Setting(FieldSettings.Component, "level-three")))
            .AddComponent("level-three", type => type.Field("next", "component", field => field.Setting(FieldSettings.Component, "level-four")))
            .AddComponent("level-four", type => type.Field("title", "text"));

        ShouldReportOnly(builder, "FCMS1207", "component 'level-one' nests 4 levels of components and blocks (level-one > level-two > level-three > level-four)");
    }

    [Fact]
    public void NestingThroughBlocksCountsToo()
    {
        var builder = new SchemaBuilder()
            .AddBlock("columns", type => type.Field("cells", "blocks", field => field.Setting(FieldSettings.Blocks, Names("cell"))))
            .AddBlock("cell", type => type.Field("card", "component", field => field.Setting(FieldSettings.Component, "card")))
            .AddComponent("card", type => type.Field("badge", "component", field => field.Setting(FieldSettings.Component, "badge")))
            .AddComponent("badge", type => type.Field("icon", "component", field => field.Setting(FieldSettings.Component, "icon")))
            .AddComponent("icon", type => type.Field("name", "text"));

        ShouldReportOnly(builder, "FCMS1207", "block 'cell' nests 4 levels of components and blocks (cell > card > badge > icon)");
    }

    [Fact]
    public void BlockThatAllowsItselfIsFCMS1207()
    {
        var builder = new SchemaBuilder()
            .AddBlock("columns", type => type.Field("cells", "blocks", field => field.Setting(FieldSettings.Blocks, Names("columns"))));

        ShouldReportOnly(builder, "FCMS1207", "columns > columns nests block 'columns' inside itself");
    }

    [Fact]
    public void LocalizableFieldOnAnUnlocalizedTypeIsFCMS1208()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("title", "text", field => field.Localizable()));

        ShouldReportOnly(builder, "FCMS1208", "field article.title is localizable, but collection 'article' is not localized");
    }

    [Fact]
    public void LocalizableFieldOnAComponentIsFCMS1208()
    {
        var builder = new SchemaBuilder().AddComponent("seo", type => type
            .Field("metaTitle", "text", field => field.Localizable()));

        ShouldReportOnly(builder, "FCMS1208", "seo.metaTitle");
    }

    [Fact]
    public void LocalizableFieldOnALocalizedTypeIsAllowed()
    {
        var model = new SchemaBuilder().AddCollection("article", type => type
            .Localized()
            .Field("title", "text", field => field.Localizable())).Build();

        model.FindContentType("article")!.FindField("title")!.Localizable.ShouldBeTrue();
    }

    [Fact]
    public void ReverseRelationNameDeclaredTwiceIsFCMS1209()
    {
        var builder = WithAuthor(new SchemaBuilder())
            .AddCollection("article", type => type.Field("author", "relation", field => field.Relation("author", "works", false)))
            .AddCollection("book", type => type.Field("writer", "relation", field => field.Relation("author", "works", false)));

        ShouldReportOnly(builder, "FCMS1209", "reverse relation name 'works' on author is declared by both article.author and book.writer");
    }

    [Fact]
    public void ReverseRelationNameThatIsAFieldOfTheTargetIsFCMS1209()
    {
        var builder = WithAuthor(new SchemaBuilder())
            .AddCollection("article", type => type.Field("author", "relation", field => field.Relation("author", "name", false)));

        ShouldReportOnly(builder, "FCMS1209", "reverse relation name 'name' of article.author is also a field of author");
    }

    [Fact]
    public void SameReverseNameOnDifferentTargetsIsAllowed()
    {
        var model = WithAuthor(new SchemaBuilder())
            .AddCollection("editor", type => type.Field("name", "text"))
            .AddCollection("article", type => type
                .Field("author", "relation", field => field.Relation("author", "articles", false))
                .Field("editor", "relation", field => field.Relation("editor", "articles", false)))
            .Build();

        model.FindContentType("author")!.ReverseRelations.Single().Name.ShouldBe("articles");
        model.FindContentType("editor")!.ReverseRelations.Single().Name.ShouldBe("articles");
    }

    [Fact]
    public void ComponentThatDoesNotExistIsFCMS1210()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("seo", "component", field => field.Setting(FieldSettings.Component, "seo")));

        ShouldReportOnly(builder, "FCMS1210", "component 'seo' used by article.seo does not exist");
    }

    [Fact]
    public void BlockThatDoesNotExistIsFCMS1210()
    {
        var builder = new SchemaBuilder()
            .AddBlock("hero", type => type.Field("heading", "text"))
            .AddCollection("article", type => type.Field("sections", "blocks", field => field.Setting(FieldSettings.Blocks, Names("hero", "quote"))));

        ShouldReportOnly(builder, "FCMS1210", "block 'quote' used by article.sections does not exist");
    }

    [Fact]
    public void SelectWithoutOptionsIsFCMS1211()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type.Field("category", "select"));

        ShouldReportOnly(builder, "FCMS1211", "select field article.category");
    }

    [Fact]
    public void RelationWithoutTargetIsFCMS1211()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type.Field("author", "relation"));

        ShouldReportOnly(builder, "FCMS1211", "relation field article.author has no target");
    }

    [Fact]
    public void RelationTargetOnAnotherFieldTypeIsFCMS1211()
    {
        var builder = WithAuthor(new SchemaBuilder())
            .AddCollection("article", type => type.Field("title", "text", field => field.Relation("author")));

        ShouldReportOnly(builder, "FCMS1211", "article.title has a relation target");
    }

    [Fact]
    public void BlocksFieldWithoutBlocksIsFCMS1211()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type.Field("sections", "blocks"));

        ShouldReportOnly(builder, "FCMS1211", "blocks field article.sections must list");
    }

    [Theory]
    [InlineData(5, 2)]
    [InlineData(-1, 10)]
    [InlineData(null, 0)]
    public void InvalidLengthLimitIsFCMS1211(int? min, int? max)
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("title", "text", field => field.Length(min, max)));

        ShouldReportOnly(builder, "FCMS1211", "invalid length limit");
    }

    [Fact]
    public void MinimumAboveMaximumIsFCMS1211()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("rating", "decimal", field => field.Range(10, 1)));

        ShouldReportOnly(builder, "FCMS1211", "minimum value of 10 above its maximum of 1");
    }

    [Fact]
    public void InvalidRegularExpressionIsFCMS1211()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("code", "text", field => field.Pattern("([a-z")));

        ShouldReportOnly(builder, "FCMS1211", "invalid regular expression");
    }

    [Fact]
    public void ConditionOnAFieldThatDoesNotExistIsFCMS1212()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("videoUrl", "url", field => field.VisibleWhen(Condition.Field("category").Equals("video"))));

        ShouldReportOnly(builder, "FCMS1212", "visibleWhen of article.videoUrl refers to 'category'");
    }

    [Fact]
    public void ConditionOnTheFieldItselfIsFCMS1212()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("videoUrl", "url", field => field.RequiredWhen(Condition.Field("videoUrl").IsEmpty().Not())));

        ShouldReportOnly(builder, "FCMS1212", "requiredWhen of article.videoUrl refers to the field itself");
    }

    [Fact]
    public void ConditionInsideAndOrIsChecked()
    {
        var condition = Condition.Field("category").Equals("video").And(Condition.Field("ghost").IsEmpty());
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("category", "text")
            .Field("videoUrl", "url", field => field.VisibleWhen(condition)));

        ShouldReportOnly(builder, "FCMS1212", "'ghost'");
    }

    [Theory]
    [InlineData("Article")]
    [InlineData("blog_post")]
    [InlineData("-article")]
    [InlineData("article-")]
    [InlineData("blog--post")]
    [InlineData("1article")]
    public void TypeNameThatIsNotKebabCaseIsFCMS1214(string name)
    {
        var builder = new SchemaBuilder().AddCollection(name, type => type.Field("title", "text"));

        ShouldReportOnly(builder, "FCMS1214", $"collection '{name}' must be lower kebab-case");
    }

    [Theory]
    [InlineData("Title")]
    [InlineData("blog_title")]
    [InlineData("blog-title")]
    [InlineData("1title")]
    public void FieldNameThatIsNotCamelCaseIsFCMS1214(string name)
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type.Field(name, "text"));

        ShouldReportOnly(builder, "FCMS1214", $"field name '{name}'");
    }

    [Fact]
    public void ReverseRelationNameThatIsNotCamelCaseIsFCMS1214()
    {
        var builder = WithAuthor(new SchemaBuilder())
            .AddCollection("article", type => type.Field("author", "relation", field => field.Relation("author", "My Articles", false)));

        ShouldReportOnly(builder, "FCMS1214", "reverse relation name 'My Articles'");
    }

    [Fact]
    public void LocalizedComponentIsFCMS1215()
    {
        var builder = new SchemaBuilder().AddComponent("seo", type => type.Localized().Field("metaTitle", "text"));

        ShouldReportOnly(builder, "FCMS1215", "component 'seo' cannot be localized");
    }

    [Fact]
    public void AllErrorsAreReportedTogether()
    {
        var builder = new SchemaBuilder()
            .AddCollection("article", type => type
                .Field("id", "text")
                .Field("hero", "media")
                .Field("author", "relation", field => field.Relation("author"))
                .Field("title", "text", field => field.Localizable()));

        var exception = Fails(builder);

        exception.Errors.Select(error => error.Code).ShouldBe(["FCMS1206", "FCMS1203", "FCMS1204", "FCMS1208"]);
        exception.Message.ShouldStartWith("The schema has 4 errors:");
        exception.Message.ShouldContain("FCMS1203: field type 'media' used by article.hero is not registered");
        exception.Message.ShouldContain("FCMS1208");
    }

    [Fact]
    public void ErrorsAreInTheSameOrderWhateverTheOrderTypesWereAdded()
    {
        static SchemaBuilder Build(bool articleFirst)
        {
            var builder = new SchemaBuilder();
            void Article() => builder.AddCollection("article", type => type.Field("author", "relation", field => field.Relation("ghost")));
            void Book() => builder.AddCollection("book", type => type.Field("writer", "relation", field => field.Relation("ghost")));

            if (articleFirst)
            {
                Article();
                Book();
            }
            else
            {
                Book();
                Article();
            }

            return builder;
        }

        var first = Fails(Build(articleFirst: true)).Errors.Select(error => error.Message);
        var second = Fails(Build(articleFirst: false)).Errors.Select(error => error.Message);

        first.ShouldBe(second);
    }

    [Fact]
    public void BuildCanBeCalledAgainAfterTheSchemaIsFixed()
    {
        var builder = new SchemaBuilder().AddCollection("article", type => type
            .Field("author", "relation", field => field.Relation("author")));

        Fails(builder);
        builder.AddCollection("author", type => type.Field("name", "text"));

        builder.Build().ContentTypes.Count.ShouldBe(2);
    }
}
