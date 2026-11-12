using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Media;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using FluentCMS.Seeding;
using FluentCMS.Settings;

namespace FluentCmsApp.Blog;

// Every entry is found by its slug, which a person does not change by accident, and each step brings what it finds to the state the
// seed wants. A run that stopped halfway therefore finishes the job. State changes (publish, trash, schedule) are made only to what
// this run created, or when the seeder never ran to the end before; after a version bump existing entries stay as a person left them
// and only missing ones are created.
public class BlogSeeder : ISeeder
{
    public const string WelcomeSlug = "welcome-to-fluentcms";
    public const string TypedSlug = "typed-content-no-magic";
    public const string TourSlug = "a-tour-of-the-blog-sample";
    public const string ScheduledSlug = "fluentcms-1-0-release-notes";
    public const string TrashedSlug = "a-post-we-took-down";
    public const string AdaSlug = "ada-lovelace";
    public const string GraceSlug = "grace-hopper";
    public const string DotnetSlug = "dotnet";
    public const string HeadlessSlug = "headless-cms";
    public const string ReleasesSlug = "release-notes";
    public const string MediaTag = "blog-sample";
    public const string WelcomeHeroFile = "welcome-hero.png";
    public const string AdaAvatarFile = "ada-lovelace.png";
    public const string GraceAvatarFile = "grace-hopper.png";
    public const string LogoFile = "logo.png";
    public const string FirstScreenshotFile = "screenshot-1.png";
    public const string SecondScreenshotFile = "screenshot-2.png";

    public static readonly TimeSpan ScheduleDelay = TimeSpan.FromDays(30);

    private static readonly GetOptions Draft = new() { Status = QueryStatus.Draft, Fallback = LocaleFallback.None };

    // A 16 by 9 PNG of one colour, so that the seed has images without shipping files.
    private static readonly byte[] Image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAJCAIAAAC0SDtlAAAAFElEQVR42mPQztlAEmIY1TAoNAAALmO38Z6fgBcAAAAASUVORK5CYII=");

    private static readonly (string File, string Title, string Alt)[] Images =
    [
        (WelcomeHeroFile, "Welcome", "The FluentCMS welcome banner"),
        (AdaAvatarFile, "Ada Lovelace", "Portrait of Ada Lovelace"),
        (GraceAvatarFile, "Grace Hopper", "Portrait of Grace Hopper"),
        (LogoFile, "Logo", "The logo of the blog"),
        (FirstScreenshotFile, "Screenshot 1", "The content list of the admin"),
        (SecondScreenshotFile, "Screenshot 2", "The entry editor of the admin"),
    ];

    // A subclass can raise the version, as a release that changes the seed content would.
    protected virtual int SeedVersion => 1;

    string ISeeder.Name => typeof(BlogSeeder).FullName!;

    int ISeeder.Version => SeedVersion;

    public async Task SeedAsync(SeedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var first = context.AppliedVersion == 0;

        var images = await SeedMediaAsync(context, cancellationToken);
        await SeedSettingsAsync(context, images[LogoFile], cancellationToken);

        var authors = context.Get<IContentService<Author>>();
        var tags = context.Get<IContentService<Tag>>();
        var articles = context.Get<IContentService<Article>>();

        var ada = await EnsureAuthorAsync(authors, first, AdaSlug, "Ada Lovelace", "Writes about analytical engines and typed content.", images[AdaAvatarFile], cancellationToken);
        var grace = await EnsureAuthorAsync(authors, first, GraceSlug, "Grace Hopper", "Believes in readable code and compilers that help.", images[GraceAvatarFile], cancellationToken);

        var dotnet = await EnsureTagAsync(tags, first, DotnetSlug, "dotnet", cancellationToken);
        var headless = await EnsureTagAsync(tags, first, HeadlessSlug, "headless cms", cancellationToken);
        var releases = await EnsureTagAsync(tags, first, ReleasesSlug, "release notes", cancellationToken);

        await SeedWelcomeAsync(context, articles, first, ada, [dotnet, headless], images[WelcomeHeroFile], cancellationToken);

        var typed = await EnsureAsync(
            articles,
            TypedSlug,
            () => new Article
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
        await PublishIfDraftAsync(articles, typed, first, cancellationToken);

        await EnsureAsync(
            articles,
            TourSlug,
            () => new Article
            {
                Title = "A tour of the blog sample",
                Slug = TourSlug,
                Body = Text("A draft that is not published yet."),
                Author = ada,
                Tags = [headless],
                Category = ArticleCategory.Video,
                VideoUrl = "https://example.com/videos/blog-sample-tour",
                Sections = [new GalleryBlock { Caption = "Screenshots", Columns = 2, Images = [images[FirstScreenshotFile], images[SecondScreenshotFile]] }],
            },
            cancellationToken);

        var taken = await EnsureAsync(
            articles,
            TrashedSlug,
            () => new Article
            {
                Title = "A post we took down",
                Slug = TrashedSlug,
                Body = Text("This one was published and then moved to the trash."),
                Author = grace,
                Category = ArticleCategory.News,
            },
            cancellationToken);
        if (!taken.InTrash && (first || taken.Created))
        {
            await PublishIfDraftAsync(articles, taken, first, cancellationToken);
            await ConvergeAsync(() => articles.DeleteAsync(taken.Entry.Id, cancellationToken));
        }

        // Last, because a scheduled publish publishes the draft and shared fields as they are when it is scheduled.
        var notes = await EnsureAsync(
            articles,
            ScheduledSlug,
            () => new Article
            {
                Title = "FluentCMS 1.0 release notes",
                Slug = ScheduledSlug,
                Body = Text("Everything that is new in 1.0."),
                Author = ada,
                Tags = [releases, dotnet],
                Category = ArticleCategory.News,
                Sections = [new HeroBlock { Heading = "FluentCMS 1.0", Subheading = "Out now", Image = images[WelcomeHeroFile] }],
            },
            cancellationToken);
        if (!notes.InTrash && (first || notes.Created) && notes.Entry.Status == EntryStatus.Draft)
        {
            var at = context.Time.GetUtcNow().Add(ScheduleDelay);
            await ConvergeAsync(() => articles.ScheduleAsync(notes.Entry.Id, ScheduleRequest.At(ScheduleAction.Publish, at), cancellationToken: cancellationToken));
        }
    }

    // Each image is found by its file name among the assets with the sample's tag, so that a run finds what an earlier one
    // uploaded. The welcome banner has its alternative text in both locales.
    private static async Task<Dictionary<string, MediaRef>> SeedMediaAsync(SeedContext context, CancellationToken cancellationToken)
    {
        var media = context.Get<IMediaService>();
        var listed = await media.ListAsync(new MediaQuery { Tag = MediaTag, PageSize = 100 }, cancellationToken);
        var found = listed.Items.GroupBy(asset => asset.FileName, StringComparer.Ordinal).ToDictionary(group => group.Key, group => new MediaRef(group.Last().Id), StringComparer.Ordinal);
        foreach (var (file, title, alt) in Images.Where(image => !found.ContainsKey(image.File)))
        {
            using var content = new MemoryStream(Image, writable: false);
            var asset = (await media.UploadAsync(new MediaUpload(null, [new MediaUploadFile(file, content)]), cancellationToken))[0];
            await media.UpdateAsync(asset.Id, new MediaAssetChanges { Title = title, Alt = alt, Tags = [MediaTag] }, cancellationToken);
            found[file] = new MediaRef(asset.Id);

            if (file == WelcomeHeroFile)
            {
                using (context.InLocale(BlogSample.SecondLocale))
                {
                    await media.UpdateAsync(asset.Id, new MediaAssetChanges { Alt = "La bannière de bienvenue de FluentCMS" }, cancellationToken);
                }
            }
        }

        return found;
    }

    private static async Task SeedSettingsAsync(SeedContext context, MediaRef logo, CancellationToken cancellationToken)
    {
        var settings = context.Get<ISettings<BlogSettings>>();
        var current = await settings.GetAsync(context.Site, cancellationToken);
        if (current.SiteName != new BlogSettings().SiteName)
        {
            return;
        }

        current.SiteName = "The FluentCMS blog";
        current.Description = "Notes on typed content and headless publishing.";
        current.Logo = logo.Id;
        await settings.SetAsync(context.Site, current, cancellationToken);
    }

    private static async Task SeedWelcomeAsync(SeedContext context, IContentService<Article> articles, bool first, Ref<Author> author, RefList<Tag> tags, MediaRef hero, CancellationToken cancellationToken)
    {
        var welcome = await EnsureAsync(
            articles,
            WelcomeSlug,
            () => new Article
            {
                Title = "Welcome to FluentCMS",
                Slug = WelcomeSlug,
                Body = Text("FluentCMS is a headless CMS for .NET."),
                Author = author,
                Tags = tags,
                Hero = hero,
                Category = ArticleCategory.News,
                PublishedOn = context.Time.GetUtcNow(),
                Sections =
                [
                    new HeroBlock { Heading = "Welcome", Subheading = "A headless CMS for .NET", Image = hero },
                    new QuoteBlock { Text = "Content is data first.", Attribution = "The FluentCMS team" },
                ],
                Seo = new Seo { MetaTitle = "Welcome to FluentCMS", MetaDescription = "What FluentCMS is and who it is for." },
            },
            cancellationToken);

        if (welcome.InTrash)
        {
            return;
        }

        using (context.InLocale(BlogSample.SecondLocale))
        {
            var options = Draft with { Locale = BlogSample.SecondLocale };
            var french = await articles.GetAsync(welcome.Entry.Id, options, cancellationToken);
            var createdFrench = false;
            if (french is null)
            {
                var source = welcome.Entry;
                try
                {
                    french = await articles.UpdateAsync(
                        source.Id,
                        new Article
                        {
                            Title = "Bienvenue dans FluentCMS",
                            Slug = "bienvenue-dans-fluentcms",
                            Body = Text("FluentCMS est un CMS headless pour .NET."),
                            Author = source.Author,
                            Tags = source.Tags,
                            Hero = source.Hero,
                            Category = source.Category,
                            PublishedOn = source.PublishedOn,
                            Sections = source.Sections,
                            Seo = source.Seo,
                        },
                        expectedVersion: 0,
                        cancellationToken);
                    createdFrench = true;
                }
                catch (EntryNotFoundException)
                {
                    return;
                }
                catch (VersionConflictException)
                {
                    french = await articles.GetAsync(welcome.Entry.Id, options, cancellationToken)
                        ?? throw new InvalidOperationException("The French welcome article is missing after a version conflict.");
                }
            }

            if ((first || createdFrench) && french.Status == EntryStatus.Draft)
            {
                await ConvergeAsync(() => articles.PublishAsync(french.Id, new LocaleWriteOptions { Locale = BlogSample.SecondLocale }, cancellationToken));
            }
        }

        await PublishIfDraftAsync(articles, welcome, first, cancellationToken);
    }

    private static async Task<Ref<Author>> EnsureAuthorAsync(IContentService<Author> authors, bool first, string slug, string name, string bio, MediaRef avatar, CancellationToken cancellationToken)
    {
        var author = await EnsureAsync(authors, slug, () => new Author { Name = name, Slug = slug, Bio = Text(bio), Avatar = avatar }, cancellationToken);
        await PublishIfDraftAsync(authors, author, first, cancellationToken);
        return new Ref<Author>(author.Entry.Id);
    }

    private static async Task<Ref<Tag>> EnsureTagAsync(IContentService<Tag> tags, bool first, string slug, string name, CancellationToken cancellationToken)
    {
        var tag = await EnsureAsync(tags, slug, () => new Tag { Name = name, Slug = slug }, cancellationToken);
        await PublishIfDraftAsync(tags, tag, first, cancellationToken);
        return new Ref<Tag>(tag.Entry.Id);
    }

    // Only a draft that this run created, or any draft on a first run, is published: a person who unpublished an entry, or changed
    // it after it was published, is not overruled.
    private static async Task PublishIfDraftAsync<T>(IContentService<T> service, Found<T> found, bool first, CancellationToken cancellationToken)
        where T : ContentItem
    {
        if (!found.InTrash && (first || found.Created) && found.Entry.Status == EntryStatus.Draft)
        {
            await ConvergeAsync(() => service.PublishAsync(found.Entry.Id, cancellationToken: cancellationToken));
        }
    }

    // An entry that is gone or in the trash by the time a state step reaches it was taken there by an overlapping run or a person; the
    // step has nothing left to do.
    private static async Task ConvergeAsync(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (EntryNotFoundException)
        {
        }
    }

    // Finds the entry by slug among the live and the trashed ones, or creates it. When another instance creates it first the unique
    // slug is refused, and the entry that is there is the one wanted.
    private static async Task<Found<T>> EnsureAsync<T>(IContentService<T> service, string slug, Func<T> build, CancellationToken cancellationToken)
        where T : ContentItem
    {
        var found = await FindAsync(service, slug, cancellationToken);
        if (found is not null)
        {
            return found;
        }

        try
        {
            return new Found<T>(await service.CreateAsync(build(), cancellationToken), InTrash: false, Created: true);
        }
        catch (ValidationException exception) when (exception.Errors.Any(error => error.Code == ValidationErrorCodes.NotUnique))
        {
            var existing = await FindAsync(service, slug, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return existing;
        }
    }

    private static async Task<Found<T>?> FindAsync<T>(IContentService<T> service, string slug, CancellationToken cancellationToken)
        where T : ContentItem
    {
        var query = new ContentQuery
        {
            Filter = new FieldFilter("slug", FilterOperator.Equal, slug),
            Status = QueryStatus.Draft,
            Page = new PageRequest { Size = 1, WithTotal = false },
        };

        var live = await service.QueryAsync(query, cancellationToken);
        if (live.Items.Count > 0)
        {
            return new Found<T>(live.Items[0], InTrash: false, Created: false);
        }

        var trashed = await service.QueryTrashAsync(query, cancellationToken);
        return trashed.Items.Count > 0 ? new Found<T>(trashed.Items[0], InTrash: true, Created: false) : null;
    }

    private static RichText Text(string text) =>
        new($$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":{{JsonSerializer.Serialize(text)}}}]}]}""");

    private sealed record Found<T>(T Entry, bool InTrash, bool Created);
}
