using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Media;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;
using Author = FluentCMS.Core.Tests.Schema.Samples.Author;

namespace FluentCMS.Data.Tests.Content;

// The sample types, which is what #2706 will replace with the Blog types of FluentCMS.Testing.
public sealed class TypedContentServiceTests : IDisposable
{
    private const string Body = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hi"}]}]}""";

    private readonly ServiceDatabase _database = new(schema: SampleSchema.FromClasses().Build());

    public void Dispose() => _database.Dispose();

    private Task<TResult> AsAsync<T, TResult>(Principal principal, Func<IContentService<T>, Task<TResult>> work, string locale = "en")
        where T : ContentItem =>
        _database.AsAsync(principal, services => work(services.GetRequiredService<IContentService<T>>()), locale);

    private static GetOptions Draft => new() { Status = QueryStatus.Draft };

    private static Article Sample(Guid author, Guid tag, Guid media) => new()
    {
        Title = "Hello World",
        Body = new RichText(Body),
        Author = new Ref<Author>(author),
        Tags = [new Ref<Tag>(tag)],
        Hero = new MediaRef(media),
        Sections = [new HeroBlock { Heading = "Big", Image = new MediaRef(media) }, new QuoteBlock { Text = "Said", Attribution = "Ann" }, new GalleryBlock { Images = [new MediaRef(media)] }],
        Seo = new Seo { MetaTitle = "Meta" },
        Category = ArticleCategory.Video,
        PublishedOn = new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero),
        VideoUrl = "https://example.com/video",
    };

    [Fact]
    public async Task ASchedulePutThroughTheTypedServiceIsTheOneTheDocumentServiceKeeps()
    {
        var tag = await AsAsync<Tag, Tag>(Editor, service => service.CreateAsync(new Tag { Name = "Net Core" }, Token));
        var runAt = DateTime.UtcNow.AddDays(1);

        var info = await AsAsync<Tag, ScheduleInfo>(Editor, service => service.ScheduleAsync(tag.Id, new ScheduleRequest(ScheduleAction.Publish, runAt), null, Token));

        info.Action.ShouldBe(ScheduleAction.Publish);
        info.Version.ShouldBe(1);
        _database.Read(context => context.Set<ScheduleRow>().Single(row => row.DocumentId == tag.Id)).RunAtUtc.UtcDateTime.ShouldBe(runAt);

        await AsAsync<Tag, object?>(Editor, async service =>
        {
            await service.CancelScheduleAsync(tag.Id, ScheduleAction.Publish, null, Token);
            return null;
        });

        _database.Read(context => context.Set<ScheduleRow>().Single(row => row.DocumentId == tag.Id)).Status.ShouldBe(ScheduleRow.Cancelled);
    }

    [Fact]
    public async Task TheSampleTypesRoundTrip()
    {
        var author = await AsAsync<Author, Author>(Editor, service => service.CreateAsync(new Author { Name = "Ann", Bio = new RichText(Body), Avatar = new MediaRef(Guid.CreateVersion7()) }, Token));
        var tag = await AsAsync<Tag, Tag>(Editor, service => service.CreateAsync(new Tag { Name = "Net Core" }, Token));
        var media = Guid.CreateVersion7();

        var created = await AsAsync<Article, Article>(Editor, service => service.CreateAsync(Sample(author.Id, tag.Id, media), Token));

        created.Id.ShouldNotBe(Guid.Empty);
        created.Version.ShouldBe(1);
        created.Status.ShouldBe(EntryStatus.Draft);
        created.Locale.ShouldBe("en");
        created.CreatedAt.ShouldNotBe(default);
        created.CreatedBy.ShouldBe(Editor.Id);

        var read = (await AsAsync<Article, Article?>(Editor, service => service.GetAsync(created.Id, Draft, Token)))!;

        read.Title.ShouldBe("Hello World");
        read.Slug.ShouldBe("hello-world");
        read.Body.Json.ShouldBe(Body);
        read.Author.Id.ShouldBe(author.Id);
        read.Tags.ShouldHaveSingleItem().Id.ShouldBe(tag.Id);
        read.Hero.ShouldBe(new MediaRef(media));
        read.Seo.MetaTitle.ShouldBe("Meta");
        read.Category.ShouldBe(ArticleCategory.Video);
        read.PublishedOn.ShouldBe(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
        read.VideoUrl.ShouldBe("https://example.com/video");

        read.Sections.Count.ShouldBe(3);
        var hero = read.Sections[0].ShouldBeOfType<HeroBlock>();
        hero.Heading.ShouldBe("Big");
        hero.Image.ShouldBe(new MediaRef(media));
        var quote = read.Sections[1].ShouldBeOfType<QuoteBlock>();
        quote.Attribution.ShouldBe("Ann");
        read.Sections[2].ShouldBeOfType<GalleryBlock>().Images.ShouldBe([new MediaRef(media)]);

        var slug = await AsAsync<Tag, Tag?>(Editor, service => service.GetAsync(tag.Id, Draft, Token));
        slug!.Slug.ShouldBe("net-core");
        (await AsAsync<Author, Author?>(Editor, service => service.GetAsync(author.Id, Draft, Token)))!.Bio.Json.ShouldBe(Body);

        read.Title = "Changed";
        var updated = await AsAsync<Article, Article>(Editor, service => service.UpdateAsync(read.Id, read, read.Version, Token));

        updated.Title.ShouldBe("Changed");
        updated.Version.ShouldBe(2);
        updated.Slug.ShouldBe("hello-world");
        updated.Category.ShouldBe(ArticleCategory.Video);
        updated.Sections.Count.ShouldBe(3);
        updated.Author.Id.ShouldBe(author.Id);
        updated.UpdatedBy.ShouldBe(Editor.Id);

        var page = await AsAsync<Article, PagedResult<Article>>(Editor, service => service.QueryAsync(new ContentQuery { Status = QueryStatus.Draft }, Token));
        page.Items.ShouldHaveSingleItem().Title.ShouldBe("Changed");
        page.Total.ShouldBe(1);

        (await AsAsync<Article, long>(Editor, service => service.CountAsync(null, QueryStatus.Draft, Token))).ShouldBe(1);

        var patched = await AsAsync<Article, Article>(Editor, service => service.PatchAsync(read.Id, new() { ["videoUrl"] = null }, 2, Token));
        patched.VideoUrl.ShouldBeNull();

        await AsAsync<Article, bool>(Editor, async service =>
        {
            await service.DeleteAsync(read.Id, Token);
            return true;
        });
        (await AsAsync<Article, Article?>(Editor, service => service.GetAsync(read.Id, Draft, Token))).ShouldBeNull();
        (await AsAsync<Article, Article>(Editor, service => service.RestoreAsync(read.Id, Token))).Title.ShouldBe("Changed");
    }

    [Fact]
    public async Task ATrashedClassIsListedAndPurgedLikeADocument()
    {
        var created = await AsAsync<Tag, Tag>(Editor, service => service.CreateAsync(new Tag { Name = "Net Core" }, Token));
        await AsAsync<Tag, bool>(Editor, async service =>
        {
            await service.DeleteAsync(created.Id, Token);
            return true;
        });

        var listed = (await AsAsync<Tag, PagedResult<Tag>>(Editor, service => service.QueryTrashAsync(new ContentQuery(), Token))).Items.ShouldHaveSingleItem();
        listed.Id.ShouldBe(created.Id);
        listed.Name.ShouldBe("Net Core");

        var admin = WithRole(BuiltInRoles.SuperAdmin);
        await AsAsync<Tag, bool>(admin, async service =>
        {
            await service.PurgeAsync(created.Id, Token);
            return true;
        });
        (await AsAsync<Tag, PagedResult<Tag>>(Editor, service => service.QueryTrashAsync(new ContentQuery(), Token))).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ASingletonRoundTrips()
    {
        var created = await AsAsync<SiteSettings, SiteSettings>(Editor, service => service.CreateAsync(new SiteSettings { SiteName = "Mine" }, Token));

        (await AsAsync<SiteSettings, SiteSettings?>(Editor, service => service.GetAsync(created.Id, Draft, Token)))!.SiteName.ShouldBe("Mine");
    }

    [Fact]
    public async Task TheSystemFieldsOfAClassAreIgnoredOnAWrite()
    {
        var forged = Guid.CreateVersion7();
        var created = await AsAsync<Tag, Tag>(Editor, service => service.CreateAsync(
            new Tag
            {
                Name = "Net",
                Id = forged,
                CreatedBy = forged,
                UpdatedBy = forged,
                Version = 50,
                Status = EntryStatus.Published,
                CreatedAt = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
                PublishedAt = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
            Token));

        created.Id.ShouldNotBe(forged);
        created.CreatedBy.ShouldBe(Editor.Id);
        created.UpdatedBy.ShouldBe(Editor.Id);
        created.Version.ShouldBe(1);
        created.Status.ShouldBe(EntryStatus.Draft);
        created.CreatedAt.Year.ShouldNotBe(2000);
        created.PublishedAt.ShouldBeNull();

        created.CreatedBy = forged;
        created.Version = 1;
        var updated = await AsAsync<Tag, Tag>(Editor, service => service.UpdateAsync(created.Id, created, 1, Token));
        updated.CreatedBy.ShouldBe(Editor.Id);
    }

    [Fact]
    public async Task AWriterWithAnAllowListCanSendBackTheClassItRead()
    {
        var created = await AsAsync<Article, Article>(Editor, service => service.CreateAsync(new Article { Title = "Hello", VideoUrl = "https://example.com/v", Category = ArticleCategory.Opinion }, Token));
        var role = _database.Database.AddRoleForTest("Titles", ("content.article.read", null), ("content.article.update", new PermissionConstraints { Fields = ["title"] }));

        var read = (await AsAsync<Article, Article?>(WithRole(role), service => service.GetAsync(created.Id, Draft, Token)))!;
        read.Title = "Changed";
        var saved = await AsAsync<Article, Article>(WithRole(role), service => service.UpdateAsync(created.Id, read, read.Version, Token));

        saved.Title.ShouldBe("Changed");
        saved.Category.ShouldBe(ArticleCategory.Opinion);
        saved.VideoUrl.ShouldBe("https://example.com/v");

        read.Version = saved.Version;
        read.Category = ArticleCategory.Video;
        (await Should.ThrowAsync<ValidationException>(() => AsAsync<Article, Article>(WithRole(role), service => service.UpdateAsync(created.Id, read, read.Version, Token))))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("category");
    }

    [Fact]
    public async Task AClassThatCannotHoldTheFormIsRefused()
    {
        (await Should.ThrowAsync<ValidationException>(() => AsAsync<Article, Article?>(Editor, service => service.GetAsync(Guid.CreateVersion7(), new GetOptions { Locale = "all" }, Token))))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("locale");
        (await Should.ThrowAsync<ValidationException>(() => AsAsync<Article, PagedResult<Article>>(Editor, service => service.QueryAsync(new ContentQuery { RichTextFormat = RichTextFormats.Html }, Token))))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("richText");
    }

    [Fact]
    public async Task AClassOutsideTheSchemaFailsAtOnce()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => _database.AsAsync(
            Editor,
            services => Task.FromResult(services.GetRequiredService<IContentService<NotAType>>())));
    }

    private sealed class NotAType : ContentItem
    {
    }
}
