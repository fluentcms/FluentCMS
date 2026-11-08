using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Seeding;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Publishing;
using FluentCMS.Samples.Blog;
using FluentCMS.Schema;
using FluentCMS.Seeding;
using FluentCMS.Settings;
using FluentCMS.Sites;
using FluentCMS.Core.Sites;
using FluentCMS.Media;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Blog;

public sealed class BlogFixture : IAsyncLifetime
{
    public FluentCmsTestHost Host { get; } = NewHost();

    public TestActor Admin { get; private set; } = null!;

    public static FluentCmsTestHost NewHost() => new FluentCmsTestHost()
        .WithContentTypes([.. BlogSample.ContentTypes])
        .WithMediaDirectory()
        .Configure(cms => cms.AddBlogSample());

    public async ValueTask InitializeAsync()
    {
        var user = await Host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: TestContext.Current.CancellationToken);
        Admin = Host.As(user);
    }

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}

// One seeded host serves every test, because the tests only read.
public sealed class BlogSampleTests(BlogFixture fixture) : IClassFixture<BlogFixture>
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private FluentCmsTestHost Host => fixture.Host;

    private TestActor Admin => fixture.Admin;

    [Fact]
    public void TheTypesRegister()
    {
        var schema = Host.Services.GetRequiredService<SchemaModel>();

        schema.ContentTypes.Select(type => type.Name).ShouldBe(["article", "author", "tag"]);
        schema.Components.Select(type => type.Name).ShouldBe(["seo"]);
        schema.Blocks.Select(type => type.Name).ShouldBe(["gallery", "hero", "quote"]);
        schema.FindContentType("article")!.Localized.ShouldBeTrue();
        schema.FindContentType("tag")!.Scope.ShouldBe(ContentScope.Global);
        schema.FindContentType("article")!.FindField("author")!.ShouldNotBeNull();
    }

    [Fact]
    public async Task TheSeedReadsBackThroughTheContentService()
    {
        var articles = Admin.Content<Article>();
        var published = await articles.QueryAsync(new ContentQuery { Status = QueryStatus.Published }, Token);
        var drafts = await articles.QueryAsync(new ContentQuery { Status = QueryStatus.Draft }, Token);

        published.Items.Select(article => article.Slug).Order().ShouldBe([BlogSeeder.TypedSlug, BlogSeeder.WelcomeSlug]);
        drafts.Items.Select(article => article.Slug).Order().ShouldBe([BlogSeeder.TourSlug, BlogSeeder.ScheduledSlug, BlogSeeder.TypedSlug, BlogSeeder.WelcomeSlug]);

        var authors = await Admin.Content<Author>().QueryAsync(new ContentQuery { Status = QueryStatus.Published }, Token);
        authors.Items.Select(author => author.Name).Order().ShouldBe(["Ada Lovelace", "Grace Hopper"]);

        var tags = await Admin.Content<Tag>().QueryAsync(new ContentQuery { Status = QueryStatus.Published }, Token);
        tags.Items.Select(tag => tag.Name).Order().ShouldBe(["dotnet", "headless cms", "release notes"]);

        var welcome = published.Items.Single(article => article.Slug == BlogSeeder.WelcomeSlug);
        var ada = authors.Items.Single(author => author.Name == "Ada Lovelace");
        welcome.Author.Id.ShouldBe(ada.Id);
        welcome.Tags.Count.ShouldBe(2);
        welcome.Category.ShouldBe(ArticleCategory.News);
        welcome.Sections.Select(section => section.GetType()).ShouldBe([typeof(HeroBlock), typeof(QuoteBlock)]);
        welcome.Seo.MetaTitle.ShouldBe("Welcome to FluentCMS");
    }

    [Fact]
    public async Task TheMediaFieldsReferToTheUploadedImages()
    {
        var images = (await Admin.RunAsync<IMediaService, PagedResult<MediaAsset>>(media => media.ListAsync(new MediaQuery { Tag = BlogSeeder.MediaTag }, Token))).Items
            .ToDictionary(asset => asset.FileName, asset => new MediaRef(asset.Id));
        var articles = (await Admin.Content<Article>().QueryAsync(new ContentQuery { Status = QueryStatus.Draft }, Token)).Items;
        var authors = (await Admin.Content<Author>().QueryAsync(new ContentQuery { Status = QueryStatus.Draft }, Token)).Items;
        var settings = await Admin.RunAsync<ISettings<BlogSettings>, BlogSettings>(async blog => await blog.GetAsync(new SiteId(DefaultSite.Id), Token));

        images.Keys.Order().ShouldBe([BlogSeeder.AdaAvatarFile, BlogSeeder.GraceAvatarFile, BlogSeeder.LogoFile, BlogSeeder.FirstScreenshotFile, BlogSeeder.SecondScreenshotFile, BlogSeeder.WelcomeHeroFile]);
        var welcome = articles.Single(article => article.Slug == BlogSeeder.WelcomeSlug);
        welcome.Hero.ShouldBe(images[BlogSeeder.WelcomeHeroFile]);
        welcome.Sections.OfType<HeroBlock>().Single().Image.ShouldBe(images[BlogSeeder.WelcomeHeroFile]);
        articles.Single(article => article.Slug == BlogSeeder.TourSlug).Sections.OfType<GalleryBlock>().Single().Images
            .ShouldBe([images[BlogSeeder.FirstScreenshotFile], images[BlogSeeder.SecondScreenshotFile]]);
        authors.Single(author => author.Slug == BlogSeeder.AdaSlug).Avatar.ShouldBe(images[BlogSeeder.AdaAvatarFile]);
        authors.Single(author => author.Slug == BlogSeeder.GraceSlug).Avatar.ShouldBe(images[BlogSeeder.GraceAvatarFile]);
        settings.Logo.ShouldBe(images[BlogSeeder.LogoFile].Id);
    }

    [Fact]
    public async Task ThePopulatedHeroHasItsAltTextInTheLocaleOfTheRead()
    {
        var welcome = (await Admin.Content<Article>().QueryAsync(new ContentQuery { Status = QueryStatus.Published }, Token)).Items
            .Single(article => article.Slug == BlogSeeder.WelcomeSlug);

        var english = await Admin.Content().GetAsync("article", welcome.Id, new GetOptions { Populate = ["hero"] }, Token);
        var french = await Admin.In(BlogSample.SecondLocale).Content().GetAsync("article", welcome.Id, new GetOptions { Populate = ["hero"] }, Token);

        var hero = english.ShouldNotBeNull()["hero"].ShouldBeOfType<ContentDocument>();
        hero["alt"].ShouldBe("The FluentCMS welcome banner");
        hero["urls"].ShouldBeOfType<ContentDocument>()["original"].ShouldBeOfType<string>().ShouldStartWith($"/media/{welcome.Hero!.Value.Id:D}/original/{BlogSeeder.WelcomeHeroFile}?sig=");
        french.ShouldNotBeNull()["hero"].ShouldBeOfType<ContentDocument>()["alt"].ShouldBe("La bannière de bienvenue de FluentCMS");
    }

    [Fact]
    public async Task TheWelcomeArticleHasATranslation()
    {
        var articles = Admin.Content<Article>();
        var welcome = (await articles.QueryAsync(new ContentQuery { Status = QueryStatus.Published }, Token)).Items
            .Single(article => article.Slug == BlogSeeder.WelcomeSlug);

        var french = await Admin.In(BlogSample.SecondLocale).Content<Article>().GetAsync(
            welcome.Id,
            new GetOptions { Fallback = LocaleFallback.None },
            Token);

        french.ShouldNotBeNull();
        french.Title.ShouldBe("Bienvenue dans FluentCMS");
        french.Locale.ShouldBe(BlogSample.SecondLocale);
        french.Status.ShouldBe(EntryStatus.Published);
    }

    [Fact]
    public async Task TheTrashedArticleIsOnlyInTheTrash()
    {
        var articles = Admin.Content<Article>();

        var live = await articles.QueryAsync(new ContentQuery { Status = QueryStatus.Draft }, Token);
        var trash = await articles.QueryTrashAsync(new ContentQuery { Status = QueryStatus.Draft }, Token);

        live.Items.ShouldNotContain(article => article.Slug == BlogSeeder.TrashedSlug);
        trash.Items.Select(article => article.Slug).ShouldBe([BlogSeeder.TrashedSlug]);
    }

    [Fact]
    public async Task TheScheduledArticleIsADraftWithAPublishAtTheClockPlusTheDelay()
    {
        var articles = Admin.Content<Article>();
        var notes = (await articles.QueryAsync(new ContentQuery { Status = QueryStatus.Draft }, Token)).Items
            .Single(article => article.Slug == BlogSeeder.ScheduledSlug);

        notes.Status.ShouldBe(EntryStatus.Draft);

        var document = await Admin.RunAsync<CmsDbContext, Document>(context =>
            context.Documents.AsNoTracking().SingleAsync(row => row.DocumentId == notes.Id && row.Locale == "en"));
        document.SchedulePublishAt.ShouldNotBeNull().ShouldBe(Host.Time.GetUtcNow().Add(BlogSeeder.ScheduleDelay), TimeSpan.FromMinutes(1));
        document.ScheduleUnpublishAt.ShouldBeNull();
    }

    [Fact]
    public async Task TheSeedWroteUnderTheSeedOriginAsTheSystem()
    {
        var articles = Admin.Content<Article>();
        var welcome = (await articles.QueryAsync(new ContentQuery { Status = QueryStatus.Published }, Token)).Items
            .Single(article => article.Slug == BlogSeeder.WelcomeSlug);

        var versions = await articles.GetVersionsAsync(welcome.Id, cancellationToken: Token);

        versions.ShouldNotBeEmpty();
        versions.ShouldAllBe(version => version.Origin == Origin.Seed);
        welcome.CreatedBy.ShouldBe(Guid.Empty);
    }

    [Fact]
    public async Task TheSiteSettingsAreStored()
    {
        var settings = await Host.Services.CreateAsyncScope().ServiceProvider
            .GetRequiredService<ISettings<BlogSettings>>()
            .GetAsync(new SiteId(DefaultSite.Id), Token);

        settings.SiteName.ShouldBe("The FluentCMS blog");
        settings.ArticlesPerPage.ShouldBe(10);
        settings.Logo.ShouldNotBeNull();
    }

    [Fact]
    public async Task RunningTheSeedAgainCreatesNothing()
    {
        var before = await CountRowsAsync();

        await Admin.RunAsync<ISeedRunner, bool>(async runner =>
        {
            await runner.RunAsync(Token);
            return true;
        });

        (await CountRowsAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task AHostWithoutTheSampleSeedsNothing()
    {
        await using var host = new FluentCmsTestHost().WithContentTypes([.. BlogSample.ContentTypes]);

        var count = await host.Anonymous.RunAsync<CmsDbContext, int>(context => context.Documents.CountAsync());

        count.ShouldBe(0);
        host.Services.GetServices<IHostedService>().ShouldNotContain(service => service is SeedStartupService);
    }

    private async Task<(int Documents, int Versions, long Images)> CountRowsAsync()
    {
        var (documents, versions) = await Admin.RunAsync<CmsDbContext, (int, int)>(async context =>
            (await context.Documents.IgnoreQueryFilters().CountAsync(), await context.Set<ContentVersion>().CountAsync()));
        var images = await Admin.RunAsync<IMediaService, PagedResult<MediaAsset>>(media => media.ListAsync(new MediaQuery(), Token));
        return (documents, versions, images.Total ?? 0);
    }
}
