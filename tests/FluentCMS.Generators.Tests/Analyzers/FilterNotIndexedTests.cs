extern alias Analyzers;

using System.Globalization;
using Analyzers::FluentCMS.Analyzers;
using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;

namespace FluentCMS.Generators.Tests.Analyzers;

// FCMS0060: a filter or sort on a field without an index is reported as information where the builder is used. It stands
// for no code of schema validation, which accepts such a field.
public sealed class FilterNotIndexedTests
{
    private const string Schema = """
        [Component]
        public sealed class Seo
        {
            [Text] public string MetaTitle { get; set; } = "";

            [Text, Indexed] public string Keyword { get; set; } = "";
        }

        [Block("hero")]
        public sealed class Hero
        {
            [Text] public string Heading { get; set; } = "";
        }

        [Collection("article")]
        public sealed class Article : ContentItem
        {
            [Text] public string Title { get; set; } = "";

            [Slug, Unique] public string Slug { get; set; } = "";

            [DateTime, Indexed] public DateTimeOffset? PublishedOn { get; set; }

            [Integer] public int Rank { get; set; }

            [LongText] public string Summary { get; set; } = "";

            [Component] public Seo Seo { get; set; } = new();

            [Blocks(typeof(Hero))] public BlockList Sections { get; set; } = new();
        }

        """;

    // The builders as the generator emits them, so that the rule is checked on its own.
    private const string Builders = """
        public static class ArticleFields
        {
            public static TextField Title { get; } = new TextField("title");

            public static TextField Slug { get; } = new TextField("slug");

            public static DateField<DateTimeOffset> PublishedOn { get; } = new DateField<DateTimeOffset>("publishedOn");

            public static NumberField<long> Rank { get; } = new NumberField<long>("rank");

            public static LongTextField Summary { get; } = new LongTextField("summary");

            public static SeoComponentFields Seo { get; } = new SeoComponentFields("seo");
        }

        public sealed class SeoComponentFields(string path)
        {
            public TextField MetaTitle => new TextField(path + ".metaTitle");

            public TextField Keyword => new TextField(path + ".keyword");
        }

        """;

    [Fact]
    public Task AFieldWithoutAnIndexIsReportedWhereItsBuilderIsUsed() => Analysis.Verify<FilterIndexAnalyzer>(Analysis.Usings + Schema + Builders + """
        public static class Queries
        {
            public static FieldFilter ByTitle() => {|FCMS0060:ArticleFields.Title|}.Contains("x");

            public static SortField ByRank() => {|FCMS0060:ArticleFields.Rank|}.Descending();

            public static FieldFilter ByMetaTitle() => {|FCMS0060:ArticleFields.Seo.MetaTitle|}.Eq("x");
        }
        """);

    [Fact]
    public Task IndexedUniqueAndUnindexableFieldsAreNotReported() => Analysis.Verify<FilterIndexAnalyzer>(Analysis.Usings + Schema + Builders + """
        public static class Queries
        {
            public static FieldFilter BySlug() => ArticleFields.Slug.Eq("x");

            public static SortField Newest() => ArticleFields.PublishedOn.Descending();

            public static FieldFilter BySummary() => ArticleFields.Summary.Contains("x");

            public static FieldFilter ByKeyword() => ArticleFields.Seo.Keyword.Eq("x");
        }
        """);

    [Fact]
    public async Task TheRuleReadsTheBuildersOfTheGenerator()
    {
        const string queries = """
            public static class Queries
            {
                public static FieldFilter ByTitle() => ArticleFields.Title.Contains("x");

                public static FieldFilter BySlug() => ArticleFields.Slug.Eq("x");

                public static FieldFilter ByHeading() => ArticleFields.Sections.Hero.Heading.Eq("x");
            }
            """;
        Generation.Driver().RunGeneratorsAndUpdateCompilation(
            Generation.Compile(("Article.cs", Analysis.Usings + Schema), ("Queries.cs", Analysis.Usings + queries)), out var compilation, out _, TestContext.Current.CancellationToken);
        compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();

        var diagnostics = await Analysis.Diagnostics(compilation);

        var reported = diagnostics.ShouldHaveSingleItem();
        reported.Id.ShouldBe("FCMS0060");
        reported.Severity.ShouldBe(DiagnosticSeverity.Info);
        reported.Location.SourceTree!.FilePath.ShouldBe("Queries.cs");
        reported.GetMessage(CultureInfo.InvariantCulture).ShouldBe("The field 'title' of the collection 'article' is filtered or sorted here but is not [Indexed], so the query reads every row of the type");
    }
}
