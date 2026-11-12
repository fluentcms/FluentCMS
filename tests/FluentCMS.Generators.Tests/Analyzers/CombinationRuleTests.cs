using Xunit;

namespace FluentCMS.Generators.Tests.Analyzers;

// FCMS0001 to FCMS0016: attribute combinations the attribute reader or schema validation refuses. Each class has a
// source that is reported and the corrected source, which builds.
public sealed class KindsCombinedTests
{
    [Fact]
    public Task AClassWithTwoKindsIsReported() => Analysis.Reports("""
        [Collection("page"), Component]
        public sealed class {|FCMS0001:Page|} : ContentItem
        {
            [Text] public string Title { get; set; } = "";
        }
        """, "FCMS1215");

    [Fact]
    public Task AClassWithOneKindIsAccepted() => Analysis.Accepts("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text] public string Title { get; set; } = "";
        }
        """);
}

public sealed class WrongBaseTests
{
    [Fact]
    public Task ACollectionThatIsNotAContentItemIsReported() => Analysis.Reports("""
        [Collection("page")]
        public sealed class {|FCMS0002:Page|}
        {
            [Text] public string Title { get; set; } = "";
        }
        """, "FCMS1215");

    [Fact]
    public Task AComponentThatIsAContentItemIsReported() => Analysis.Reports("""
        [Component]
        public sealed class {|FCMS0002:Seo|} : ContentItem
        {
            [Text] public string MetaTitle { get; set; } = "";
        }
        """, "FCMS1215");

    [Fact]
    public Task TheCorrectedBasesAreAccepted() => Analysis.Accepts("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text] public string Title { get; set; } = "";
        }

        [Component]
        public sealed class Seo
        {
            [Text] public string MetaTitle { get; set; } = "";
        }
        """);
}

public sealed class PublishPolicyNotPublishedTests
{
    [Fact]
    public Task APublishPolicyOnAComponentIsReported() => Analysis.Reports("""
        [Component, {|FCMS0003:PublishPolicy(MaxVersions = 5)|}]
        public sealed class Seo
        {
            [Text] public string MetaTitle { get; set; } = "";
        }
        """, "FCMS1215");

    [Fact]
    public Task APublishPolicyOnACollectionIsAccepted() => Analysis.Accepts("""
        [Collection("page"), PublishPolicy(MaxVersions = 5)]
        public sealed class Page : ContentItem
        {
            [Text] public string Title { get; set; } = "";
        }

        [Component]
        public sealed class Seo
        {
            [Text] public string MetaTitle { get; set; } = "";
        }
        """);
}

public sealed class NoFieldTypeTests
{
    [Fact]
    public Task AModifierWithoutAFieldTypeIsReported() => Analysis.Reports("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [{|FCMS0004:Required|}] public string Title { get; set; } = "";
        }
        """, "FCMS1216");

    [Fact]
    public Task AModifierWithAFieldTypeIsAccepted() => Analysis.Accepts("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text, Required] public string Title { get; set; } = "";

            public string NotAField { get; set; } = "";
        }
        """);
}

public sealed class FieldTypesCombinedTests
{
    [Fact]
    public Task TwoFieldTypesAreReported() => Analysis.Reports("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text, {|FCMS0005:LongText|}] public string Title { get; set; } = "";
        }
        """, "FCMS1216");

    [Fact]
    public Task OneFieldTypeIsAccepted() => Analysis.Accepts("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text] public string Title { get; set; } = "";
        }
        """);
}

public sealed class NotPublicTests
{
    [Fact]
    public Task APrivateSetterIsReported() => Analysis.Reports("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text] public string {|FCMS0006:Title|} { get; private set; } = "";
        }
        """, "FCMS1216");

    [Fact]
    public Task APropertyOfABaseClassIsReportedAtItsDeclaration() => Analysis.Reports("""
        public abstract class PageBase : ContentItem
        {
            [Text] internal string {|FCMS0006:Title|} { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : PageBase;

        [Collection("post")]
        public sealed class Post : PageBase;
        """, "FCMS1216");

    [Fact]
    public Task APublicGetterAndSetterAreAccepted() => Analysis.Accepts("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text] public string Title { get; set; } = "";
        }
        """);
}

public sealed class EnumWithOptionsTests
{
    [Fact]
    public Task OptionsOnAnEnumAreReported() => Analysis.Reports("""
        public enum Kind { News, Blog }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [{|FCMS0007:Select("news", "blog")|}] public Kind Kind { get; set; }
        }
        """, "FCMS1216");

    [Fact]
    public Task AnEnumWithoutOptionsIsAccepted() => Analysis.Accepts("""
        public enum Kind { News, Blog }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Select] public Kind Kind { get; set; }
        }
        """);
}

public sealed class NoOptionsTests
{
    [Fact]
    public Task AStringWithoutOptionsIsReported() => Analysis.Reports("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [{|FCMS0008:Select|}] public string Kind { get; set; } = "";
        }
        """, "FCMS1216");

    [Fact]
    public Task AStringWithOptionsIsAccepted() => Analysis.Accepts("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Select("news", "blog")] public string Kind { get; set; } = "";
        }
        """);
}

public sealed class NotAReferenceTests
{
    [Fact]
    public Task ARelationThatIsAGuidIsReported() => Analysis.Reports("""
        [Collection("author")]
        public sealed class Author : ContentItem
        {
            [Text] public string Name { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [{|FCMS0009:Relation|}] public Guid Author { get; set; }
        }
        """, "FCMS1216");

    [Fact]
    public Task ARefIsAccepted() => Analysis.Accepts("""
        [Collection("author")]
        public sealed class Author : ContentItem
        {
            [Text] public string Name { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Relation] public Ref<Author> Author { get; set; }

            [Relation] public RefList<Author> Editors { get; set; } = [];
        }
        """);
}

public sealed class NotAComponentTests
{
    [Fact]
    public Task AClassThatIsNotAComponentIsReported() => Analysis.Reports("""
        public sealed class Address
        {
            [Text] public string Street { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [{|FCMS0010:Component|}] public Address Address { get; set; } = new();
        }
        """, "FCMS1210");

    [Fact]
    public Task AComponentAndAListOfThemAreAccepted() => Analysis.Accepts("""
        [Component]
        public sealed class Address
        {
            [Text] public string Street { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Component] public Address Address { get; set; } = new();

            [Component] public List<Address> Branches { get; set; } = [];
        }
        """);
}

public sealed class NoBlockTypesTests
{
    [Fact]
    public Task BlocksWithoutTypesAreReported() => Analysis.Reports("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [{|FCMS0011:Blocks|}] public BlockList Body { get; set; } = new();
        }
        """, "FCMS1211");

    [Fact]
    public Task BlocksWithATypeAreAccepted() => Analysis.Accepts("""
        [Block("hero")]
        public sealed class Hero
        {
            [Text] public string Heading { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Blocks(typeof(Hero))] public BlockList Body { get; set; } = new();
        }
        """);
}

public sealed class NotABlockTests
{
    [Fact]
    public Task AClassThatIsNotABlockIsReported() => Analysis.Reports("""
        public sealed class Hero
        {
            [Text] public string Heading { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [{|FCMS0012:Blocks(typeof(Hero))|}] public BlockList Body { get; set; } = new();
        }
        """, "FCMS1210");

    [Fact]
    public Task ABlockIsAccepted() => Analysis.Accepts("""
        [Block("hero")]
        public sealed class Hero
        {
            [Text] public string Heading { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Blocks(typeof(Hero))] public BlockList Body { get; set; } = new();
        }
        """);
}

public sealed class TwoMaximumLengthsTests
{
    [Fact]
    public Task TwoDifferentMaximumsAreReported() => Analysis.Reports("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text(MaxLength = 100), {|FCMS0013:Length(Max = 200)|}] public string Title { get; set; } = "";
        }
        """, "FCMS1216");

    [Fact]
    public Task OneMaximumIsAccepted() => Analysis.Accepts("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text(MaxLength = 100), Length(Min = 2, Max = 100)] public string Title { get; set; } = "";
        }
        """);
}

public sealed class ListOfSingleValueTests
{
    [Fact]
    public Task AListOfTextIsReported() => Analysis.Reports("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [{|FCMS0014:Text|}] public List<string> Keywords { get; set; } = [];
        }
        """, "FCMS1211");

    [Fact]
    public Task AListOfSelectIsAccepted() => Analysis.Accepts("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Select("news", "blog")] public List<string> Keywords { get; set; } = [];
        }
        """);
}

public sealed class UniqueWithoutColumnTests
{
    [Fact]
    public Task AUniqueRelationIsReported() => Analysis.Reports("""
        [Collection("author")]
        public sealed class Author : ContentItem
        {
            [Text] public string Name { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Relation, {|FCMS0015:Unique|}] public Ref<Author> Author { get; set; }

            [RichText, {|FCMS0015:Unique|}] public RichText Body { get; set; } = RichText.Empty;
        }
        """, "FCMS1211");

    [Fact]
    public Task AUniqueFieldOfAComponentIsReportedWhereItIsDeclared() => Analysis.Reports("""
        [Component]
        public sealed class Seo
        {
            [Json, {|FCMS0015:Unique|}] public System.Text.Json.JsonElement? Data { get; set; }
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Component] public Seo Seo { get; set; } = new();
        }
        """, "FCMS1211");

    [Fact]
    public Task AUniqueTextIsAccepted() => Analysis.Accepts("""
        [Component]
        public sealed class Seo
        {
            [Json] public System.Text.Json.JsonElement? Data { get; set; }

            [Text, Unique] public string Key { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text, Unique] public string Title { get; set; } = "";

            [Component] public Seo Seo { get; set; } = new();
        }
        """);
}

public sealed class ReverseOutsideContentTypeTests
{
    [Fact]
    public Task AReverseSideInAComponentIsReported() => Analysis.Reports("""
        [Collection("author")]
        public sealed class Author : ContentItem
        {
            [Text] public string Name { get; set; } = "";
        }

        [Component]
        public sealed class Byline
        {
            [Relation({|FCMS0016:Reverse = "bylines"|})] public Ref<Author> Author { get; set; }
        }
        """, "FCMS1217");

    [Fact]
    public Task AReverseSideInACollectionIsAccepted() => Analysis.Accepts("""
        [Collection("author")]
        public sealed class Author : ContentItem
        {
            [Text] public string Name { get; set; } = "";
        }

        [Component]
        public sealed class Byline
        {
            [Relation] public Ref<Author> Author { get; set; }
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Relation(Reverse = "pages")] public Ref<Author> Author { get; set; }
        }
        """);
}
