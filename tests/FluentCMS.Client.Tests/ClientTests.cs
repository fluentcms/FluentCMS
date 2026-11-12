using System.Net;
using FluentCMS.Client.Tests.Blog;
using FluentCMS.Plugins.Media;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using BlogSeeder = FluentCMS.Samples.Blog.BlogSeeder;

namespace FluentCMS.Client.Tests;

// The seeded Blog sample, read with a delivery key and written with a management key.
public sealed class BlogFixture : IAsyncLifetime
{
    private readonly DirectoryInfo _media = Directory.CreateTempSubdirectory("fluentcms-client-media-");

    public BlogFixture()
    {
        Host = new FluentCmsTestHost()
            .WithContentTypes([.. FluentCMS.Samples.Blog.BlogSample.ContentTypes])
            .WithServices(services => services.PostConfigure<MediaOptions>(options => options.Local.Root = _media.FullName))
            .Configure(cms => FluentCMS.Samples.Blog.BlogSample.AddBlogSample(cms));
    }

    public FluentCmsTestHost Host { get; }

    public HttpClient DeliveryHttp { get; private set; } = null!;

    public FluentCmsClient Delivery { get; private set; } = null!;

    public FluentCmsClient Management { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        DeliveryHttp = Host.AsKey(await Host.CreateApiKeyAsync(ApiKeyClass.Delivery, "Super Admin", cancellationToken: TestContext.Current.CancellationToken));
        Delivery = new FluentCmsClient(DeliveryHttp);
        Management = new FluentCmsClient(Host.AsKey(await Host.CreateApiKeyAsync(ApiKeyClass.Management, "Super Admin", cancellationToken: TestContext.Current.CancellationToken)));
    }

    public async ValueTask DisposeAsync()
    {
        DeliveryHttp.Dispose();
        await Host.DisposeAsync();
        _media.Delete(recursive: true);
    }
}

public sealed class ClientTests(BlogFixture blog) : IClassFixture<BlogFixture>
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public void TheBuildersReadAsInAHost()
    {
        ArticleFields.Title.Contains("x").ShouldBe(new FieldFilter("title", FilterOperator.Contains, "x"));
        ArticleFields.Seo.MetaTitle.Eq("x").ShouldBe(new FieldFilter("seo.metaTitle", FilterOperator.Equal, "x"));
        ArticleFields.Sections.Hero.Heading.Eq("x").ShouldBe(new FieldFilter("sections.hero.heading", FilterOperator.Equal, "x"));
        ArticleFields.Sections.HasHero().ShouldBe(new FieldFilter("sections", FilterOperator.HasBlock, "hero"));
        ArticleFields.PublishedOn.Descending().ShouldBe(new SortField("publishedOn", Descending: true));
    }

    [Fact]
    public async Task ATypedQueryReturnsTheEntriesOfTheTestHost()
    {
        var opinions = await blog.Delivery.ListAsync<Article>(new ContentQuery { Filter = ArticleFields.Category.Eq("opinion") }, Token);
        opinions.Items.Select(article => article.Slug).ShouldBe([BlogSeeder.TypedSlug]);
        opinions.Items.Single().Sections!.Single().ShouldBeOfType<QuoteBlock>().Text.ShouldBe("Make illegal states unrepresentable.");

        var withHero = await blog.Delivery.ListAsync<Article>(
            new ContentQuery { Filter = new And(ArticleFields.Sections.HasHero(), ArticleFields.Sections.Hero.Heading.Eq("Welcome")), Sort = [ArticleFields.Title.Ascending()], PageSize = 10 },
            Token);
        withHero.Items.Select(article => article.Slug).ShouldBe([BlogSeeder.WelcomeSlug]);
        withHero.PageSize.ShouldBe(10);
    }

    [Fact]
    public async Task APopulatedReadHasItsRelationsMediaAndBlocks()
    {
        var welcome = await blog.Delivery.GetByAsync<Article>("slug", BlogSeeder.WelcomeSlug, new ContentQuery { Populate = ["author", "tags"] }, Token);

        welcome.Author!.Entry!.Name.ShouldBe("Ada Lovelace");
        welcome.Author.Type.ShouldBe("author");
        welcome.Author.Entry.Avatar!.Type.ShouldBe("media");
        welcome.Tags!.Count.ShouldBe(2);
        welcome.Tags.ShouldAllBe(tag => tag.Entry != null && tag.Entry.Name != null);
        welcome.Seo!.MetaTitle.ShouldBe("Welcome to FluentCMS");
        welcome.Body!.Value.GetProperty("type").GetString().ShouldBe("doc");
        var hero = welcome.Sections!.First().ShouldBeOfType<HeroBlock>();
        hero.Heading.ShouldBe("Welcome");
        hero.Id.ShouldNotBeNullOrEmpty();
        hero.Image!.Id.ShouldNotBe(Guid.Empty);
        hero.Image.Type.ShouldBe("media");

        var byId = await blog.Delivery.GetAsync<Article>(welcome.Id, cancellationToken: Token);
        byId.Author!.Entry.ShouldBeNull();
        byId.Author.Id.ShouldBe(welcome.Author.Id);
    }

    // The same list as @fluentcms/client's tests, and every case on a field of the Blog sample is a query the API answers.
    [Fact]
    public async Task TheFiltersOfTheSharedListGiveItsQueryStringsAndTheDeliveryApiAnswersThem()
    {
        List<string> answered = [];
        foreach (var item in FilterCases.Filters)
        {
            var query = new ContentQuery { Filter = FilterCases.Filter(item) }.ToQueryString();
            query.ShouldBe(item.GetProperty("query").GetString(), item.ToString());
            if (FilterCases.ReachesTheApi(item))
            {
                answered.Add(query);
            }
        }

        foreach (var item in FilterCases.Queries)
        {
            var query = FilterCases.Query(item).ToQueryString();
            query.ShouldBe(item.GetProperty("expect").GetString(), item.GetProperty("name").GetString());
            if (FilterCases.ReachesTheApi(item))
            {
                answered.Add(query);
            }
        }

        answered.Count.ShouldBeGreaterThan(20);
        foreach (var query in answered)
        {
            using var response = await blog.DeliveryHttp.GetAsync(new Uri($"api/delivery/v1/article?{query}", UriKind.Relative), Token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{query}: {await response.Content.ReadAsStringAsync(Token)}");
        }
    }

    [Fact]
    public async Task AProblemSurfacesWithItsCode()
    {
        var missing = await Should.ThrowAsync<FluentCmsProblemException>(() => blog.Delivery.GetAsync<Article>(Guid.NewGuid(), cancellationToken: Token));
        missing.Status.ShouldBe(HttpStatusCode.NotFound);
        missing.Code.ShouldNotBeNull().ShouldStartWith("FCMS");
        missing.Message.ShouldStartWith(missing.Code + ": ");

        var unknown = await Should.ThrowAsync<FluentCmsProblemException>(() => blog.Delivery.ListAsync<Article>(new ContentQuery { Filter = new FieldFilter("nope", FilterOperator.Equal, "x") }, Token));
        unknown.Status.ShouldBe(HttpStatusCode.BadRequest);
        unknown.Code.ShouldBe("FCMS2202");
        unknown.Errors.ShouldContain(error => error.Path == "filter[nope][eq]");
    }

    [Fact]
    public async Task TheContentGroupCreatesUpdatesPublishesUnpublishesAndDeletes()
    {
        var author = await blog.Management.CreateAsync(new Author { Name = "Lin Clark" }, cancellationToken: Token);
        author.Status.ShouldBe("draft");
        author.Slug.ShouldBe("lin-clark");

        author.Name = "Lin Clark-Kent";
        var updated = await blog.Management.UpdateAsync(author.Id, author, author.Version!.Value, cancellationToken: Token);
        updated.Name.ShouldBe("Lin Clark-Kent");

        var stale = await Should.ThrowAsync<FluentCmsProblemException>(() => blog.Management.UpdateAsync(author.Id, author, author.Version!.Value, cancellationToken: Token));
        stale.Status.ShouldBe(HttpStatusCode.PreconditionFailed);

        (await blog.Management.PublishAsync<Author>(author.Id, cancellationToken: Token)).Entry.Status.ShouldBe("published");

        var article = await blog.Management.CreateAsync(
            new Article
            {
                Title = "Written by a client",
                Category = "news",
                Author = new Relation<Author>(author.Id),
                Sections = [new HeroBlock { Heading = "From C#" }, new QuoteBlock { Text = "Typed all the way." }],
            },
            cancellationToken: Token);
        await blog.Management.PublishAsync<Article>(article.Id, cancellationToken: Token);

        var read = await blog.Delivery.GetAsync<Article>(article.Id, new ContentQuery { Populate = ["author"] }, Token);
        read.Author!.Entry!.Name.ShouldBe("Lin Clark-Kent");
        read.Sections!.Select(block => block.GetType()).ShouldBe([typeof(HeroBlock), typeof(QuoteBlock)]);

        (await blog.Management.UnpublishAsync<Article>(article.Id, cancellationToken: Token)).Id.ShouldBe(article.Id);
        (await Should.ThrowAsync<FluentCmsProblemException>(() => blog.Delivery.GetAsync<Article>(article.Id, cancellationToken: Token))).Status.ShouldBe(HttpStatusCode.NotFound);

        await blog.Management.DeleteAsync<Article>(article.Id, Token);
        (await Should.ThrowAsync<FluentCmsProblemException>(() => blog.Management.PublishAsync<Article>(article.Id, cancellationToken: Token))).Code.ShouldNotBeNull().ShouldStartWith("FCMS");
    }
}
