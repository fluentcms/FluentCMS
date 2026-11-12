using Xunit;

namespace FluentCMS.Generators.Tests.Analyzers;

// FCMS0020 to FCMS0050: names, slug sources, relation targets and localization. Each class has a source that is reported
// and the corrected source, which builds.
public sealed class ReservedFieldNameTests
{
    [Fact]
    public Task ASystemFieldNameIsReported() => Analysis.Reports("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text] public string {|FCMS0020:Fallbacks|} { get; set; } = "";
        }
        """, "FCMS1206");

    [Fact]
    public Task TheDiscriminatorOfABlockIsReported() => Analysis.Reports("""
        [Block("hero")]
        public sealed class Hero
        {
            [Text] public string {|FCMS0020:Type|} { get; set; } = "";
        }
        """, "FCMS1206");

    [Fact]
    public Task AFieldOnASystemColumnIsReported() => Analysis.Reports("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text] public string {|FCMS0020:SiteId|} { get; set; } = "";
        }
        """, "FCMS1206");

    [Fact]
    public Task AReservedReverseRelationIsReported() => Analysis.Reports("""
        [Collection("author")]
        public sealed class Author : ContentItem
        {
            [Text] public string Name { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Relation({|FCMS0020:Reverse = "status"|})] public Ref<Author> Author { get; set; }
        }
        """, "FCMS1206");

    [Fact]
    public Task FreeNamesAreAccepted() => Analysis.Accepts("""
        [Collection("author")]
        public sealed class Author : ContentItem
        {
            [Text] public string Name { get; set; } = "";
        }

        [Block("hero")]
        public sealed class Hero
        {
            [Text] public string Heading { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text] public string Title { get; set; } = "";

            [Text] public string Site { get; set; } = "";

            [Relation(Reverse = "pages")] public Ref<Author> Author { get; set; }

            [Blocks(typeof(Hero))] public BlockList Body { get; set; } = new();
        }
        """);
}

public sealed class ReservedTypeNameTests
{
    [Fact]
    public Task ACollectionOnASystemTableIsReported() => Analysis.Reports("""
        [Collection({|FCMS0021:"user"|})]
        public sealed class Person : ContentItem
        {
            [Text] public string Name { get; set; } = "";
        }
        """, "FCMS1206");

    [Fact]
    public Task AComponentMayTakeTheName() => Analysis.Accepts("""
        [Component("user")]
        public sealed class Person
        {
            [Text] public string Name { get; set; } = "";
        }

        [Collection("author")]
        public sealed class Author : ContentItem
        {
            [Text] public string Name { get; set; } = "";
        }
        """);
}

public sealed class TypeNameTooLongTests
{
    [Fact]
    public Task ATypeNameOverTheLimitIsReported() => Analysis.Reports("""
        [Collection({|FCMS0022:"abcdefghijabcdefghijabcdefghijabcdefghijabcdefghijabcdefghijabcde"|})]
        public sealed class Page : ContentItem
        {
            [Text] public string Title { get; set; } = "";
        }
        """, "FCMS1218");

    [Fact]
    public Task ALocaleTableOverTheLimitIsReported() => Analysis.Reports("""
        [Collection({|FCMS0022:"abcdefghijabcdefghijabcdefghijabcdefghijabcdefghijabcde"|}, Localized = true)]
        public sealed class Page : ContentItem
        {
            [Text] public string Title { get; set; } = "";
        }
        """, "FCMS1218");

    [Fact]
    public Task TheSameNameWithoutALocaleTableIsAccepted() => Analysis.Accepts("""
        [Collection("abcdefghijabcdefghijabcdefghijabcdefghijabcdefghijabcde")]
        public sealed class Page : ContentItem
        {
            [Text] public string Title { get; set; } = "";
        }
        """);
}

public sealed class FieldNameTooLongTests
{
    [Fact]
    public Task AFieldNameOverTheLimitIsReported() => Analysis.Reports("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text] public string {|FCMS0023:AbcdefghijAbcdefghijAbcdefghijAbcdefghijAbcdefghijAbcdefghijAbcde|} { get; set; } = "";
        }
        """, "FCMS1218");

    [Fact]
    public Task AFlattenedColumnOverTheLimitIsReportedAtTheInnerField() => Analysis.Reports("""
        [Component]
        public sealed class Seo
        {
            [Text] public string {|FCMS0023:AlternativeTitleForSocialMediaSharingCard|} { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Component] public Seo SearchEngineOptimisation { get; set; } = new();
        }
        """, "FCMS1218");

    [Fact]
    public Task AShorterHolderIsAccepted() => Analysis.Accepts("""
        [Component]
        public sealed class Seo
        {
            [Text] public string AlternativeTitleForSocialMediaSharingCard { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Component] public Seo Seo { get; set; } = new();
        }
        """);
}

public sealed class SlugSourceMissingTests
{
    [Fact]
    public Task ASourceThatIsNoFieldIsReported() => Analysis.Reports("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text] public string Title { get; set; } = "";

            [Slug({|FCMS0030:Source = "Headline"|})] public string Slug { get; set; } = "";
        }
        """, "FCMS1205");

    [Fact]
    public Task ASlugThatIsItsOwnSourceIsReported() => Analysis.Reports("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Slug({|FCMS0030:Source = nameof(Slug)|})] public string Slug { get; set; } = "";
        }
        """, "FCMS1205");

    [Fact]
    public Task ASourceThatIsAFieldIsAccepted() => Analysis.Accepts("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text] public string Title { get; set; } = "";

            [Slug(Source = nameof(Title))] public string Slug { get; set; } = "";
        }
        """);
}

public sealed class RelationTargetNotContentTypeTests
{
    [Fact]
    public Task ARelationToAnUnmarkedContentItemIsReported() => Analysis.Reports("""
        public sealed class Draft : ContentItem
        {
            [Text] public string Title { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Relation] public {|FCMS0040:Ref<Draft>|} Draft { get; set; }
        }
        """, "FCMS1204");

    [Fact]
    public Task ARelationToAPlainClassIsReported() => Analysis.Reports("""
        public sealed class Author : ContentItem;

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Relation] public {|FCMS0040:RefList<Author>|} Authors { get; set; } = [];
        }
        """, "FCMS1204");

    [Fact]
    public Task ARelationToACollectionOrSingletonIsAccepted() => Analysis.Accepts("""
        [Collection("author")]
        public sealed class Author : ContentItem
        {
            [Text] public string Name { get; set; } = "";
        }

        [Singleton("settings")]
        public sealed class Settings : ContentItem
        {
            [Text] public string Name { get; set; } = "";
        }

        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Relation] public RefList<Author> Authors { get; set; } = [];

            [Relation] public Ref<Settings> Settings { get; set; }
        }
        """);
}

public sealed class LocalizableOnUnlocalizedTypeTests
{
    [Fact]
    public Task ALocalizableFieldOfAnUnlocalizedCollectionIsAWarning() => Analysis.Reports("""
        [Collection("page")]
        public sealed class Page : ContentItem
        {
            [Text, {|FCMS0050:Localizable|}] public string Title { get; set; } = "";
        }
        """, "FCMS1208");

    [Fact]
    public Task ALocalizableFieldOfAComponentIsAWarning() => Analysis.Reports("""
        [Component]
        public sealed class Seo
        {
            [Text, {|FCMS0050:Localizable|}] public string MetaTitle { get; set; } = "";
        }
        """, "FCMS1208");

    [Fact]
    public Task ALocalizableFieldOfALocalizedCollectionIsAccepted() => Analysis.Accepts("""
        [Component]
        public sealed class Seo
        {
            [Text] public string MetaTitle { get; set; } = "";
        }

        [Collection("page", Localized = true)]
        public sealed class Page : ContentItem
        {
            [Text, Localizable] public string Title { get; set; } = "";

            [Component, Localizable] public Seo Seo { get; set; } = new();
        }
        """);
}
