using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using FluentCMS.Seeding;
using FluentCMS.Settings;

namespace FluentCMS.Samples.Blog;

// Finds each entry by a natural key (the name of an author or tag, the English slug of an article), so a second run creates nothing.
public sealed class BlogSeeder : ISeeder
{
    public const string WelcomeSlug = "welcome-to-fluentcms";
    public const string TypedSlug = "typed-content-no-magic";
    public const string TourSlug = "a-tour-of-the-blog-sample";
    public const string ScheduledSlug = "fluentcms-1-0-release-notes";
    public const string TrashedSlug = "a-post-we-took-down";

    public static readonly TimeSpan ScheduleDelay = TimeSpan.FromDays(30);

    public async Task SeedAsync(SeedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await SeedSettingsAsync(context, cancellationToken);

        var authors = context.Get<IContentService<Author>>();
        var tags = context.Get<IContentService<Tag>>();
        var articles = context.Get<IContentService<Article>>();

        var ada = await EnsureAuthorAsync(authors, "Ada Lovelace", "Writes about analytical engines and typed content.", cancellationToken);
        var grace = await EnsureAuthorAsync(authors, "Grace Hopper", "Believes in readable code and compilers that help.", cancellationToken);

        var dotnet = await EnsureTagAsync(tags, "dotnet", cancellationToken);
        var headless = await EnsureTagAsync(tags, "headless cms", cancellationToken);
        var releases = await EnsureTagAsync(tags, "release notes", cancellationToken);

        await EnsureWelcomeAsync(context, articles, ada, [dotnet, headless], cancellationToken);

        if (await FindAsync(articles, TypedSlug, cancellationToken) is null)
        {
            var typed = await articles.CreateAsync(
                new Article
                {
                    Title = "Typed content, no magic",
                    Slug = TypedSlug,
                    Body = Text("Content types are plain classes. The schema, the storage and the API come from them."),
                    Author = grace,
                    Tags = [dotnet],
                    Category = ArticleCategory.Opinion,
                    Sections = [new QuoteBlock { Text = "Make illegal states unrepresentable.", Attribution = "A common saying" }],
                    Seo = new Seo { MetaTitle = "Typed content, no magic" },
                },
                cancellationToken);
            await articles.PublishAsync(typed.Id, cancellationToken: cancellationToken);
        }

        if (await FindAsync(articles, TourSlug, cancellationToken) is null)
        {
            await articles.CreateAsync(
                new Article
                {
                    Title = "A tour of the blog sample",
                    Slug = TourSlug,
                    Body = Text("A draft that is not published yet."),
                    Author = ada,
                    Tags = [headless],
                    Category = ArticleCategory.Video,
                    VideoUrl = "https://example.com/videos/blog-sample-tour",
                    Sections = [new GalleryBlock { Caption = "Screenshots", Columns = 2 }],
                },
                cancellationToken);
        }

        if (await FindAsync(articles, TrashedSlug, cancellationToken) is null)
        {
            var taken = await articles.CreateAsync(
                new Article
                {
                    Title = "A post we took down",
                    Slug = TrashedSlug,
                    Body = Text("This one was published and then moved to the trash."),
                    Author = grace,
                    Category = ArticleCategory.News,
                },
                cancellationToken);
            await articles.PublishAsync(taken.Id, cancellationToken: cancellationToken);
            await articles.DeleteAsync(taken.Id, cancellationToken);
        }

        // Last, because a scheduled publish publishes the draft and shared fields as they are when it is scheduled.
        if (await FindAsync(articles, ScheduledSlug, cancellationToken) is null)
        {
            var notes = await articles.CreateAsync(
                new Article
                {
                    Title = "FluentCMS 1.0 release notes",
                    Slug = ScheduledSlug,
                    Body = Text("Everything that is new in 1.0."),
                    Author = ada,
                    Tags = [releases, dotnet],
                    Category = ArticleCategory.News,
                    Sections = [new HeroBlock { Heading = "FluentCMS 1.0", Subheading = "Out now" }],
                },
                cancellationToken);
            var at = context.Time.GetUtcNow().UtcDateTime.Add(ScheduleDelay);
            await articles.ScheduleAsync(notes.Id, new ScheduleRequest(ScheduleAction.Publish, at), cancellationToken: cancellationToken);
        }
    }

    private static async Task SeedSettingsAsync(SeedContext context, CancellationToken cancellationToken)
    {
        var settings = context.Get<ISettings<BlogSettings>>();
        var current = await settings.GetAsync(context.Site, cancellationToken);
        if (current.SiteName != new BlogSettings().SiteName)
        {
            return;
        }

        current.SiteName = "The FluentCMS blog";
        current.Description = "Notes on typed content and headless publishing.";
        await settings.SetAsync(context.Site, current, cancellationToken);
    }

    private static async Task EnsureWelcomeAsync(SeedContext context, IContentService<Article> articles, Ref<Author> author, RefList<Tag> tags, CancellationToken cancellationToken)
    {
        if (await FindAsync(articles, WelcomeSlug, cancellationToken) is not null)
        {
            return;
        }

        var created = await articles.CreateAsync(
            new Article
            {
                Title = "Welcome to FluentCMS",
                Slug = WelcomeSlug,
                Body = Text("FluentCMS is a headless CMS for .NET."),
                Author = author,
                Tags = tags,
                Category = ArticleCategory.News,
                PublishedOn = context.Time.GetUtcNow(),
                Sections =
                [
                    new HeroBlock { Heading = "Welcome", Subheading = "A headless CMS for .NET" },
                    new QuoteBlock { Text = "Content is data first.", Attribution = "The FluentCMS team" },
                ],
                Seo = new Seo { MetaTitle = "Welcome to FluentCMS", MetaDescription = "What FluentCMS is and who it is for." },
            },
            cancellationToken);

        using (context.InLocale(BlogSample.SecondLocale))
        {
            await articles.UpdateAsync(
                created.Id,
                new Article
                {
                    Title = "Bienvenue dans FluentCMS",
                    Slug = "bienvenue-dans-fluentcms",
                    Body = Text("FluentCMS est un CMS headless pour .NET."),
                    Author = author,
                    Tags = tags,
                    Category = ArticleCategory.News,
                    PublishedOn = created.PublishedOn,
                    Sections = created.Sections,
                    Seo = created.Seo,
                },
                expectedVersion: 0,
                cancellationToken);
            await articles.PublishAsync(created.Id, BlogSample.SecondLocale, cancellationToken: cancellationToken);
        }

        await articles.PublishAsync(created.Id, cancellationToken: cancellationToken);
    }

    private static async Task<Ref<Author>> EnsureAuthorAsync(IContentService<Author> authors, string name, string bio, CancellationToken cancellationToken)
    {
        var found = await FindAsync(authors, "name", name, cancellationToken);
        if (found is not null)
        {
            return new Ref<Author>(found.Id);
        }

        var created = await authors.CreateAsync(new Author { Name = name, Bio = Text(bio) }, cancellationToken);
        await authors.PublishAsync(created.Id, cancellationToken: cancellationToken);
        return new Ref<Author>(created.Id);
    }

    private static async Task<Ref<Tag>> EnsureTagAsync(IContentService<Tag> tags, string name, CancellationToken cancellationToken)
    {
        var found = await FindAsync(tags, "name", name, cancellationToken);
        if (found is not null)
        {
            return new Ref<Tag>(found.Id);
        }

        var created = await tags.CreateAsync(new Tag { Name = name }, cancellationToken);
        await tags.PublishAsync(created.Id, cancellationToken: cancellationToken);
        return new Ref<Tag>(created.Id);
    }

    private static Task<Article?> FindAsync(IContentService<Article> articles, string slug, CancellationToken cancellationToken) =>
        FindAsync(articles, "slug", slug, cancellationToken);

    // A trashed entry counts as found, because it still holds its slug.
    private static async Task<T?> FindAsync<T>(IContentService<T> service, string field, string value, CancellationToken cancellationToken)
        where T : ContentItem
    {
        var query = new ContentQuery
        {
            Filter = new FieldFilter(field, FilterOperator.Equal, value),
            Status = QueryStatus.Draft,
            Page = new PageRequest { Size = 1, WithTotal = false },
        };

        var live = await service.QueryAsync(query, cancellationToken);
        if (live.Items.Count > 0)
        {
            return live.Items[0];
        }

        var trashed = await service.QueryTrashAsync(query, cancellationToken);
        return trashed.Items.Count > 0 ? trashed.Items[0] : null;
    }

    private static RichText Text(string text) =>
        new($$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":{{JsonSerializer.Serialize(text)}}}]}]}""");
}
