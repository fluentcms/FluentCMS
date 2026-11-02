using FluentCMS.Core.Schema;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Media;
using FluentCMS.Schema;
using Shouldly;
using Xunit;
using CollectionAttribute = FluentCMS.Schema.CollectionAttribute;

namespace FluentCMS.Core.Tests.Schema;

public class AttributeReaderTests
{
    private static SchemaBuilder Builder() => SampleSchema.NewBuilder();

    private static SchemaType Shapes() =>
        Builder().AddContentType<ShapesClass>().AddContentType<Tag>().Build().FindContentType("shapes")!;

    private static IEnumerable<string> Strings(object value) => (IEnumerable<string>)value;

    private static void ShouldReport(Type type, string code, string messagePart)
    {
        var builder = Builder().AddContentType(type);

        var errors = Should.Throw<SchemaValidationException>(() => builder.Build()).Errors;

        errors.Select(error => error.Code).ShouldBe([code]);
        errors[0].Message.ShouldContain(messagePart);
    }

    [Fact]
    public void TypeNameDefaultsToTheKebabCaseClassName()
    {
        var model = Builder().AddContentType<BlogPost>().AddContentType<FaqItem>().Build();

        model.ContentTypes.Select(type => type.Name).ShouldBe(["blog-post", "faq-item"]);
    }

    [Fact]
    public void FieldNameIsTheCamelCasePropertyName()
    {
        var model = Builder().AddContentType<BlogPost>().Build();

        model.FindContentType("blog-post")!.Fields.Select(field => field.Name).ShouldBe(["title", "url", "faqCount"]);
    }

    [Fact]
    public void PropertiesWithoutFieldAttributesAreNotFields()
    {
        var model = SampleSchema.FromClasses().Build();

        model.FindContentType("author")!.FindField("articles").ShouldBeNull();
        model.FindContentType("article")!.FindField("id").ShouldBeNull();
    }

    [Fact]
    public void PropertyTypeDecidesWhetherAFieldHoldsAList()
    {
        var shapes = Shapes();

        shapes.FindField("categories")!.Multiple.ShouldBeTrue();
        Strings(shapes.FindField("categories")!.Settings[FieldSettings.Options]).ShouldBe(["news", "opinion", "video"]);
        shapes.FindField("choice")!.Multiple.ShouldBeFalse();
        shapes.FindField("gallery")!.Multiple.ShouldBeTrue();
        shapes.FindField("logo")!.Multiple.ShouldBeFalse();
        shapes.FindField("addresses")!.Multiple.ShouldBeTrue();
        shapes.FindField("addresses")!.Settings[FieldSettings.Component].ShouldBe("seo");
        shapes.FindField("related")!.Multiple.ShouldBeTrue();
    }

    [Fact]
    public void SelectListsTheOptionsOfAnAttributeWhenThePropertyIsNotAnEnum()
    {
        var choice = Shapes().FindField("choice")!;

        Strings(choice.Settings[FieldSettings.Options]).ShouldBe(["red", "green"]);
    }

    [Fact]
    public void ModifiersAreCarriedIntoTheModel()
    {
        var price = Shapes().FindField("price")!;

        price.Settings[FieldSettings.Precision].ShouldBe(10L);
        price.Settings[FieldSettings.Scale].ShouldBe(2L);
        price.Minimum.ShouldBe(0m);
        price.Maximum.ShouldBe(99.5m);
        price.Hidden.ShouldBeTrue();
        price.ReadOnly.ShouldBeTrue();
        price.DefaultValue.ShouldBe(1L);
        price.Display.ShouldNotBeNull().Label.ShouldBe("Unit price");
        price.Display.Width.ShouldBe(6);
        price.FormerNames.ShouldBe(["cost", "amount"]);
    }

    [Fact]
    public void TextLengthCombinesTheTextAttributeAndLength()
    {
        var code = Shapes().FindField("code")!;

        code.MinLength.ShouldBe(2);
        code.MaxLength.ShouldBe(10);
        code.Pattern.ShouldBe("^[A-Z]+$");
    }

    [Fact]
    public void RangeWithoutABoundLeavesItOpen()
    {
        var count = Shapes().FindField("count")!;

        count.Minimum.ShouldBe(1m);
        count.Maximum.ShouldBeNull();
    }

    [Fact]
    public void EnumDefaultValueIsStoredAsTheOptionName()
    {
        var kind = Shapes().FindField("kind")!;

        kind.DefaultValue.ShouldBe("opinion");
    }

    [Fact]
    public void InheritedPropertiesComeBeforeTheOnesOfTheClass()
    {
        var model = Builder().AddContentType<DerivedPage>().Build();

        model.FindContentType("derived-page")!.Fields.Select(field => field.Name).ShouldBe(["headline", "body"]);
    }

    [Fact]
    public void AddingAContentTypeAddsItsComponentsAndBlocksButNotItsRelationTargets()
    {
        var builder = Builder().AddContentType<Article>();

        var errors = Should.Throw<SchemaValidationException>(() => builder.Build()).Errors;

        errors.Select(error => error.Code).ShouldBe(["FCMS1204", "FCMS1204"]);
        errors.Select(error => error.Message).ShouldBe(
        [
            "relation target 'author' of article.author is not a collection or singleton",
            "relation target 'tag' of article.tags is not a collection or singleton",
        ]);

        builder.AddContentType<Author>().AddContentType<Tag>().Build().Components.Select(type => type.Name).ShouldBe(["seo"]);
    }

    [Fact]
    public void AddingTheSameClassTwiceIsHarmless()
    {
        var tag = typeof(Tag);
        var builder = Builder().AddContentType<Tag>().AddContentType<Tag>().AddContentType(tag);

        builder.Build().ContentTypes.Count.ShouldBe(1);
    }

    [Fact]
    public void ClassWithoutAKindAttributeIsFCMS1215()
    {
        ShouldReport(typeof(Unmarked), "FCMS1215", "class Unmarked is not marked with");
    }

    [Fact]
    public void ClassWithTwoKindAttributesIsFCMS1215()
    {
        ShouldReport(typeof(TwoKinds), "FCMS1215", "more than one of");
    }

    [Fact]
    public void CollectionThatIsNotAContentItemIsFCMS1215()
    {
        ShouldReport(typeof(NotAContentItem), "FCMS1215", "collection NotAContentItem must derive from ContentItem");
    }

    [Fact]
    public void ComponentThatIsAContentItemIsFCMS1215()
    {
        ShouldReport(typeof(ContentItemComponent), "FCMS1215", "component ContentItemComponent must not derive from ContentItem");
    }

    [Fact]
    public void AbstractClassIsFCMS1215()
    {
        ShouldReport(typeof(AbstractCollection), "FCMS1215", "concrete, non-generic class");
    }

    [Fact]
    public void TwoFieldTypeAttributesAreFCMS1216()
    {
        ShouldReport(typeof(TwoFieldTypes), "FCMS1216", "TwoFieldTypes.Body has more than one field type attribute");
    }

    [Fact]
    public void ModifierWithoutAFieldTypeIsFCMS1216()
    {
        ShouldReport(typeof(LonelyModifier), "FCMS1216", "LonelyModifier.Body has schema attributes but no field type attribute");
    }

    [Fact]
    public void FieldWithoutAPublicSetterIsFCMS1216()
    {
        ShouldReport(typeof(ReadOnlyProperty), "FCMS1216", "ReadOnlyProperty.Body needs a public getter and setter");
    }

    [Fact]
    public void RelationOnAnotherTypeIsFCMS1216()
    {
        ShouldReport(typeof(BadRelation), "FCMS1216", "relation property BadRelation.Author must be a Ref<T> or a RefList<T>");
    }

    [Fact]
    public void RelationToAClassThatIsNotAContentTypeIsFCMS1204()
    {
        ShouldReport(typeof(RelationToUnmarked), "FCMS1204", "relation target Unmarked of RelationToUnmarked.Author");
    }

    [Fact]
    public void ConditionMemberThatDoesNotExistIsFCMS1212()
    {
        ShouldReport(typeof(MissingCondition), "FCMS1212", "condition Nope of MissingCondition.Link is not a static property or field");
    }

    [Fact]
    public void ConditionMemberOfAnotherTypeIsFCMS1212()
    {
        ShouldReport(typeof(WrongConditionType), "FCMS1212", "condition NotACondition of WrongConditionType.Link is not a Condition");
    }

    [Fact]
    public void ComponentFieldOfAClassThatIsNotAComponentIsFCMS1210()
    {
        ShouldReport(typeof(BadComponent), "FCMS1210", "component field BadComponent.Seo has type Unmarked, which is not marked with [Component]");
    }

    [Fact]
    public void BlocksFieldWithAClassThatIsNotABlockIsFCMS1210()
    {
        ShouldReport(typeof(BadBlocks), "FCMS1210", "BadBlocks.Sections lists Seo, which is not marked with [Block]");
    }

    [Fact]
    public void BlocksFieldWithoutBlocksIsFCMS1211()
    {
        ShouldReport(typeof(EmptyBlocks), "FCMS1211", "blocks field EmptyBlocks.Sections must list the block types");
    }

    [Fact]
    public void SelectWithoutOptionsOnAStringIsFCMS1216()
    {
        ShouldReport(typeof(SelectWithoutOptions), "FCMS1216", "SelectWithoutOptions.Kind is not an enum, so [Select] must list its options");
    }

    [Fact]
    public void SelectWithOptionsOnAnEnumIsFCMS1216()
    {
        ShouldReport(typeof(SelectEnumWithOptions), "FCMS1216", "SelectEnumWithOptions.Kind is an enum");
    }

    [Fact]
    public void TwoMaximumLengthsAreFCMS1216()
    {
        ShouldReport(typeof(ConflictingLengths), "FCMS1216", "ConflictingLengths.Code sets the maximum length to 10 and to 20");
    }

    [Fact]
    public void DefaultValueOfAnUnsupportedTypeIsFCMS1216()
    {
        ShouldReport(typeof(UnsupportedDefault), "FCMS1216", "UnsupportedDefault.Code: A value of type");
    }

    [Fact]
    public void ClassesInAnAssemblyWithoutKindAttributesAreIgnored()
    {
        var builder = Builder().AddContentTypesFromAssembly(SampleSchema.SamplesOnly());

        builder.Build().ContentTypes.Count.ShouldBe(4);
    }

    [Collection]
    private sealed class BlogPost : ContentItem
    {
        [Text]
        public string Title { get; set; } = "";

        [Url]
        public string? Url { get; set; }

        [Integer]
        public int FaqCount { get; set; }
    }

    [Collection]
    private sealed class FaqItem : ContentItem;

    [Collection("shapes")]
    private sealed class ShapesClass : ContentItem
    {
        [Select]
        public List<ArticleCategory> Categories { get; set; } = [];

        [Select("red", "green")]
        public string Choice { get; set; } = "";

        [Media]
        public MediaRefList Gallery { get; set; } = [];

        [Media]
        public MediaRef? Logo { get; set; }

        [Component]
        public List<Seo> Addresses { get; set; } = [];

        [Relation]
        public RefList<Tag> Related { get; set; } = [];

        [Decimal(Precision = 10, Scale = 2), Range(0, 99.5), Hidden, ReadOnly, DefaultValue(1)]
        [Display(Label = "Unit price", Width = 6), FormerNames("Cost", "amount")]
        public decimal Price { get; set; }

        [Text(MaxLength = 10), Length(Min = 2), RegularExpression("^[A-Z]+$")]
        public string Code { get; set; } = "";

        [Integer, Range(1, double.PositiveInfinity)]
        public int Count { get; set; }

        [Select, DefaultValue(ArticleCategory.Opinion)]
        public ArticleCategory Kind { get; set; }
    }

    private abstract class BasePage : ContentItem
    {
        [Text]
        public string Headline { get; set; } = "";
    }

    [Collection]
    private sealed class DerivedPage : BasePage
    {
        [LongText]
        public string Body { get; set; } = "";
    }

    private sealed class Unmarked : ContentItem;

    [Collection, Singleton]
    private sealed class TwoKinds : ContentItem;

    [Collection]
    private sealed class NotAContentItem;

    [Component]
    private sealed class ContentItemComponent : ContentItem;

    [Collection]
    private abstract class AbstractCollection : ContentItem;

    [Collection]
    private sealed class TwoFieldTypes : ContentItem
    {
        [Text, LongText]
        public string Body { get; set; } = "";
    }

    [Collection]
    private sealed class LonelyModifier : ContentItem
    {
        [Required]
        public string Body { get; set; } = "";
    }

    [Collection]
    private sealed class ReadOnlyProperty : ContentItem
    {
        [Text]
        public string Body { get; } = "";
    }

    [Collection]
    private sealed class BadRelation : ContentItem
    {
        [Relation]
        public string Author { get; set; } = "";
    }

    [Collection]
    private sealed class RelationToUnmarked : ContentItem
    {
        [Relation]
        public Ref<Unmarked> Author { get; set; }
    }

    [Collection]
    private sealed class MissingCondition : ContentItem
    {
        [VisibleWhen("Nope"), Url]
        public string? Link { get; set; }
    }

    [Collection]
    private sealed class WrongConditionType : ContentItem
    {
        public static string NotACondition => "category";

        [VisibleWhen(nameof(NotACondition)), Url]
        public string? Link { get; set; }
    }

    [Collection]
    private sealed class BadComponent : ContentItem
    {
        [Component]
        public Unmarked Seo { get; set; } = new();
    }

    [Collection]
    private sealed class BadBlocks : ContentItem
    {
        [Blocks(typeof(Seo))]
        public BlockList Sections { get; set; } = [];
    }

    [Collection]
    private sealed class EmptyBlocks : ContentItem
    {
        [Blocks]
        public BlockList Sections { get; set; } = [];
    }

    [Collection]
    private sealed class SelectWithoutOptions : ContentItem
    {
        [Select]
        public string Kind { get; set; } = "";
    }

    [Collection]
    private sealed class SelectEnumWithOptions : ContentItem
    {
        [Select("a")]
        public ArticleCategory Kind { get; set; }
    }

    [Collection]
    private sealed class ConflictingLengths : ContentItem
    {
        [Text(MaxLength = 10), Length(Max = 20)]
        public string Code { get; set; } = "";
    }

    [Collection]
    private sealed class UnsupportedDefault : ContentItem
    {
        [Text, DefaultValue(typeof(string))]
        public string Code { get; set; } = "";
    }
}
