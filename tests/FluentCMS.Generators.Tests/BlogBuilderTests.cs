using System.Reflection;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Schema;
using FluentCMS.Generators.Emit;
using FluentCMS.Generators.Snapshot;
using FluentCMS.Plugins.Media;
using FluentCMS.Samples.Blog;
using FluentCMS.Schema;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Generators.Tests;

public sealed class BlogFixture : IAsyncLifetime
{
    private readonly DirectoryInfo _media = Directory.CreateTempSubdirectory("fluentcms-generators-media-");

    public BlogFixture()
    {
        Host = new FluentCmsTestHost()
            .WithContentTypes([.. BlogSample.ContentTypes])
            .WithServices(services => services.PostConfigure<MediaOptions>(options => options.Local.Root = _media.FullName))
            .Configure(cms => cms.AddBlogSample());
    }

    public FluentCmsTestHost Host { get; }

    public IContentService<Article> Articles { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var user = await Host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: TestContext.Current.CancellationToken);
        Articles = Host.As(user).Content<Article>();
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        _media.Delete(recursive: true);
    }
}

// The builders the generator emitted into the Blog sample, against the seeded sample.
public sealed class BlogBuilderTests(BlogFixture fixture) : IClassFixture<BlogFixture>
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private static readonly Guid Asset = Guid.NewGuid();

    private static readonly string[] NewsAndVideo = ["news", "video"];

    private SchemaModel Schema => fixture.Host.Services.GetRequiredService<SchemaModel>();

    [Fact]
    public void ABuilderGivesTheFilterWrittenByHand()
    {
        ArticleFields.Title.Contains("x").ShouldBe(new FieldFilter("title", FilterOperator.Contains, "x"));
        ArticleFields.Category.In("news", "video").ShouldBe(new FieldFilter("category", FilterOperator.In, NewsAndVideo));
        ArticleFields.PublishedOn.Descending().ShouldBe(new SortField("publishedOn", Descending: true));
        AuthorFields.Name.Ascending().ShouldBe(new SortField("name"));
    }

    [Fact]
    public void AComponentIsReachedByPathAndABlockListHasHelpersForItsBlockTypes()
    {
        ArticleFields.Seo.MetaTitle.Eq("y").ShouldBe(new FieldFilter("seo.metaTitle", FilterOperator.Equal, "y"));
        ArticleFields.Seo.MetaDescription.Descending().ShouldBe(new SortField("seo.metaDescription", Descending: true));

        ArticleFields.Sections.HasHero().ShouldBe(new FieldFilter("sections", FilterOperator.HasBlock, "hero"));
        ArticleFields.Sections.HasQuote().ShouldBe(ArticleFields.Sections.HasBlock("quote"));
        ArticleFields.Sections.Hero.Heading.Eq("Hi").ShouldBe(new FieldFilter("sections.hero.heading", FilterOperator.Equal, "Hi"));
        ArticleFields.Sections.Quote.Text.Contains("code").ShouldBe(new FieldFilter("sections.quote.text", FilterOperator.Contains, "code"));
        ArticleFields.Sections.Gallery.Columns.Eq(3).ShouldBe(new FieldFilter("sections.gallery.columns", FilterOperator.Equal, 3L));
    }

    [Fact]
    public async Task AQueryBuiltFromTheBuildersReturnsTheSameEntries()
    {
        (ContentFilter Typed, ContentFilter ByHand)[] filters =
        [
            (ArticleFields.Title.Contains("e"), new FieldFilter("title", FilterOperator.Contains, "e")),
            (ArticleFields.Category.Eq("news"), new FieldFilter("category", FilterOperator.Equal, "news")),
            (ArticleFields.Sections.HasQuote(), new FieldFilter("sections", FilterOperator.HasBlock, "quote")),
            (ArticleFields.Sections.Hero.Heading.Contains("e"), new FieldFilter("sections.hero.heading", FilterOperator.Contains, "e")),
            (ArticleFields.Seo.MetaTitle.IsNotNull(), new FieldFilter("seo.metaTitle", FilterOperator.IsNotNull)),
            (ArticleFields.PublishedOn.Lt(DateTimeOffset.UtcNow), new FieldFilter("publishedOn", FilterOperator.LessThan, DateTimeOffset.UtcNow)),
            (new Or(ArticleFields.Hero.In(Asset), ArticleFields.Tags.IsNotNull()), new Or(new FieldFilter("hero", FilterOperator.In, new[] { Asset }), new FieldFilter("tags", FilterOperator.IsNotNull))),
        ];

        var all = await Slugs(new ContentQuery { Status = QueryStatus.Draft });
        all.Count.ShouldBeGreaterThan(1);

        var matched = 0;
        foreach (var (typed, byHand) in filters)
        {
            var expected = await Slugs(new ContentQuery { Status = QueryStatus.Draft, Filter = byHand });
            (await Slugs(new ContentQuery { Status = QueryStatus.Draft, Filter = typed })).ShouldBe(expected);
            matched += expected.Count;
        }

        matched.ShouldBeGreaterThan(0);

        var sorted = await Slugs(new ContentQuery { Status = QueryStatus.Draft, Sort = [ArticleFields.PublishedOn.Descending(), ArticleFields.Title.Ascending()] });
        sorted.ShouldBe(await Slugs(new ContentQuery { Status = QueryStatus.Draft, Sort = [new SortField("publishedOn", Descending: true), new SortField("title")] }));
    }

    [Fact]
    public void TheFieldsTheGeneratorEmitsAreTheFieldsOfTheSchema()
    {
        foreach (var type in Schema.ContentTypes)
        {
            Paths(Builder(type, "Fields"), instance: null).ShouldBe(Filterable(type).Select(field => field.Name), $"collection {type.Name}");
        }

        foreach (var type in Schema.Components)
        {
            Paths(Builder(type, "ComponentFields"), Activator.CreateInstance(Builder(type, "ComponentFields"), "c"))
                .ShouldBe(Filterable(type).Select(field => "c." + field.Name), $"component {type.Name}");
        }

        foreach (var type in Schema.Blocks)
        {
            Paths(Builder(type, "BlockFields"), Activator.CreateInstance(Builder(type, "BlockFields"), "b"))
                .ShouldBe(type.Fields.Where(field => BlockScalars.Contains(field.FieldType)).Select(field => $"b.{type.Name}.{field.Name}"), $"block {type.Name}");
        }

        var blocks = Schema.FindContentType("article")!.FindField("sections")!.Settings[FieldSettings.Blocks].ShouldBeAssignableTo<IEnumerable<string>>()!;
        typeof(ArticleFields.SectionsBlocks).GetMethods().Where(method => method.Name.StartsWith("Has", StringComparison.Ordinal) && method.Name != "HasBlock")
            .Select(method => method.Name)
            .ShouldBe(blocks.Select(block => "Has" + FieldsEmitter.Pascal(block)), ignoreOrder: true);
    }

    [Fact]
    public void TheEmitterGivesTheSameTextFromSymbolsAndFromTheSnapshot() =>
        Generation.ShouldEmitTheSameText("BlogSources", fixture.Host.Services, typeof(Article).Namespace!);

    // The field types a filter reaches inside a block (Q7).
    private static readonly string[] BlockScalars = ["text", "longText", "slug", "email", "url", "select", "integer", "decimal", "boolean", "date", "dateTime"];

    private static IEnumerable<SchemaField> Filterable(SchemaType type) =>
        type.Fields.Where(field => !(field.FieldType == "component" && field.Multiple));

    private static Type Builder(SchemaType type, string suffix) =>
        typeof(Article).Assembly.GetType($"{typeof(Article).Namespace}.{FieldsEmitter.Pascal(type.Name)}{suffix}", throwOnError: true)!;

    // The path of every field of a builder: a QueryField's own, and the path a component builder was made with.
    private static List<string> Paths(Type builder, object? instance)
    {
        var flags = BindingFlags.Public | (instance is null ? BindingFlags.Static : BindingFlags.Instance);
        return
        [
            .. builder.GetProperties(flags).Select(property => property.GetValue(instance) switch
            {
                QueryField field => field.Path,
                { } component => (string)component.GetType().GetField("_path", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(component)!,
                _ => throw new InvalidOperationException(property.Name),
            }),
        ];
    }

    private async Task<List<string>> Slugs(ContentQuery query) =>
        [.. (await fixture.Articles.QueryAsync(query with { Page = new PageRequest { Size = 100 } }, Token)).Items.Select(article => article.Slug)];
}
