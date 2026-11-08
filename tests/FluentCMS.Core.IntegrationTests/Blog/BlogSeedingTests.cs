using System.Reflection;
using System.Runtime.ExceptionServices;
using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Seeding;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Publishing;
using FluentCMS.Samples.Blog;
using FluentCMS.Schema;
using FluentCMS.Seeding;
using FluentCMS.Settings;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Blog;

// What the host does around a seeder: it runs once per version, it is claimed, and a run that stopped halfway is finished by the next.
public sealed class BlogSeedingTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private static readonly SiteId Site = new(DefaultSite.Id);

    private static FluentCmsTestHost ManualHost(bool file = false)
    {
        var host = new FluentCmsTestHost()
            .WithContentTypes([.. BlogSample.ContentTypes])
            .WithMediaDirectory()
            .Configure(cms =>
            {
                cms.AddBlogSample();
                cms.Seeding.RunOnStart = false;
            });
        return file ? host.UseSqlite((string?)null) : host;
    }

    private static async Task<TestActor> AdminOf(FluentCmsTestHost host) =>
        host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));

    private static async Task RunAsync(FluentCmsTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISeedRunner>().RunAsync(Token);
    }

    private static async Task ChangeSeedingStateAsync(FluentCmsTestHost host, Func<ISettings<SeedingState>, Task> change)
    {
        await using var scope = host.Services.CreateAsyncScope();
        using var operation = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Cli, Site);
        await change(scope.ServiceProvider.GetRequiredService<ISettings<SeedingState>>());
    }

    private static async Task<List<T>> AllAsync<T>(TestActor admin)
        where T : ContentItem =>
        [.. (await admin.Content<T>().QueryAsync(new ContentQuery { Status = QueryStatus.Draft }, Token)).Items];

    private static async Task<int> DocumentsAsync(TestActor admin) =>
        await admin.RunAsync<CmsDbContext, int>(context => context.Documents.IgnoreQueryFilters().CountAsync());

    [Fact]
    public async Task TwoRunsAtOnceOnOneDatabaseSeedOnceAndNeitherThrows()
    {
        await using var host = ManualHost(file: true);
        var admin = await AdminOf(host);

        var first = Task.Run(() => RunAsync(host), Token);
        var second = Task.Run(() => RunAsync(host), Token);
        await Task.WhenAll(first, second);

        (await AllAsync<Author>(admin)).Count.ShouldBe(2);
        (await AllAsync<Tag>(admin)).Count.ShouldBe(3);
        (await AllAsync<Article>(admin)).Count.ShouldBe(4);
        (await admin.Content<Article>().QueryTrashAsync(new ContentQuery { Status = QueryStatus.Draft }, Token)).Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TwoLocaleAdditionsAtOnceAddItOnce()
    {
        await using var host = ManualHost(file: true);
        var admin = await AdminOf(host);

        Task Ensure() => Task.Run(() => admin.RunAsync<SiteManager, bool>(async sites =>
        {
            await sites.EnsureLocaleAsync(DefaultSite.Id, "de", Token);
            return true;
        }), Token);
        await Task.WhenAll(Ensure(), Ensure(), Ensure());

        var count = await admin.RunAsync<CmsDbContext, int>(context => context.Set<SiteLocale>().CountAsync(row => row.Code == "de"));
        count.ShouldBe(1);
    }

    [Fact]
    public async Task AddingALocaleIsInTheActivityLog()
    {
        await using var host = ManualHost();
        await RunAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<ActivityRow>().AsNoTracking()
            .Where(row => row.Action == "site.locale-added").ToListAsync(Token);

        var row = rows.ShouldHaveSingleItem();
        row.Locale.ShouldBe("fr");
        row.Origin.ShouldBe(Origin.Seed);
    }

    [Fact]
    public async Task APurgedEntryDoesNotComeBackAtTheNextBoot()
    {
        await using var host = BlogFixture.NewHost();
        var admin = await AdminOf(host);
        var trashed = (await admin.Content<Article>().QueryTrashAsync(new ContentQuery { Status = QueryStatus.Draft }, Token)).Items.Single();
        await admin.Content<Article>().PurgeAsync(trashed.Id, Token);

        await RunAsync(host);

        (await admin.Content<Article>().QueryTrashAsync(new ContentQuery { Status = QueryStatus.Draft }, Token)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task RenamedAuthorsAndTagsAreNotSeededAgain()
    {
        await using var host = BlogFixture.NewHost();
        var admin = await AdminOf(host);
        var ada = (await AllAsync<Author>(admin)).Single(author => author.Slug == BlogSeeder.AdaSlug);
        ada.Name = "Ada King";
        await admin.Content<Author>().UpdateAsync(ada.Id, ada, ada.Version, Token);
        var dotnet = (await AllAsync<Tag>(admin)).Single(tag => tag.Slug == BlogSeeder.DotnetSlug);
        dotnet.Name = "C# and .NET";
        await admin.Content<Tag>().UpdateAsync(dotnet.Id, dotnet, dotnet.Version, Token);
        var before = await DocumentsAsync(admin);

        await RunAsync(host);

        (await AllAsync<Author>(admin)).Select(author => author.Name).Order().ShouldBe(["Ada King", "Grace Hopper"]);
        (await AllAsync<Tag>(admin)).Count.ShouldBe(3);
        (await DocumentsAsync(admin)).ShouldBe(before);
    }

    [Fact]
    public async Task ChangedSettingsAreLeftAlone()
    {
        await using var host = BlogFixture.NewHost();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            using var operation = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Cli, Site);
            await scope.ServiceProvider.GetRequiredService<ISettings<BlogSettings>>()
                .SetAsync(Site, new BlogSettings { SiteName = "My blog", Description = "Mine", ArticlesPerPage = 5 }, Token);
        }

        await RunAsync(host);

        await using var read = host.Services.CreateAsyncScope();
        var now = await read.ServiceProvider.GetRequiredService<ISettings<BlogSettings>>().GetAsync(Site, Token);
        now.Description.ShouldBe("Mine");
        now.ArticlesPerPage.ShouldBe(5);
    }

    [Fact]
    public async Task ARunThatStoppedAfterCreatingTheWelcomeArticleIsFinishedByTheNext()
    {
        await using var host = ManualHost()
            .Seed(new Article { Title = "Welcome to FluentCMS", Slug = BlogSeeder.WelcomeSlug, Category = ArticleCategory.News });
        var admin = await AdminOf(host);

        await RunAsync(host);

        var welcome = (await AllAsync<Article>(admin)).Single(article => article.Slug == BlogSeeder.WelcomeSlug);
        var published = await admin.Content<Article>().QueryAsync(new ContentQuery { Status = QueryStatus.Published }, Token);
        var french = await admin.In(BlogSample.SecondLocale).Content<Article>().GetAsync(welcome.Id, new GetOptions { Status = QueryStatus.Published, Fallback = LocaleFallback.None }, Token);

        published.Items.ShouldContain(article => article.Slug == BlogSeeder.WelcomeSlug);
        french.ShouldNotBeNull().Title.ShouldBe("Bienvenue dans FluentCMS");
        (await AllAsync<Article>(admin)).Count(article => article.Slug == BlogSeeder.WelcomeSlug).ShouldBe(1);
    }

    [Fact]
    public async Task ARunThatStoppedBeforeTrashingAndSchedulingIsFinishedByTheNext()
    {
        await using var host = ManualHost()
            .Seed(
                new Article { Title = "A post we took down", Slug = BlogSeeder.TrashedSlug, Category = ArticleCategory.News },
                new Article { Title = "FluentCMS 1.0 release notes", Slug = BlogSeeder.ScheduledSlug, Category = ArticleCategory.News });
        var admin = await AdminOf(host);

        await RunAsync(host);

        var live = await AllAsync<Article>(admin);
        var trash = (await admin.Content<Article>().QueryTrashAsync(new ContentQuery { Status = QueryStatus.Draft }, Token)).Items;
        trash.Select(article => article.Slug).ShouldBe([BlogSeeder.TrashedSlug]);
        live.ShouldNotContain(article => article.Slug == BlogSeeder.TrashedSlug);

        var notes = live.Single(article => article.Slug == BlogSeeder.ScheduledSlug);
        var document = await admin.RunAsync<CmsDbContext, Document>(context =>
            context.Documents.AsNoTracking().SingleAsync(row => row.DocumentId == notes.Id && row.Locale == "en"));
        document.SchedulePublishAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ADraftAuthorAndTagAreFoundBySlugAndPublished()
    {
        await using var host = ManualHost()
            .Seed(new Author { Name = "Someone else", Slug = BlogSeeder.AdaSlug })
            .Seed(new Tag { Name = "Renamed", Slug = BlogSeeder.DotnetSlug });
        var admin = await AdminOf(host);

        await RunAsync(host);

        var authors = await AllAsync<Author>(admin);
        authors.Count.ShouldBe(2);
        authors.Single(author => author.Slug == BlogSeeder.AdaSlug).Status.ShouldBe(EntryStatus.Published);
        (await AllAsync<Tag>(admin)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task ASeederRunsAgainOnlyWhenItsVersionIsRaised()
    {
        var counter = new SeedCounter();
        await using var host = Counting(counter, runOnStart: true);
        _ = host.Services;

        counter.Runs.ShouldBe(1);

        await RunAsync(host);
        counter.Runs.ShouldBe(1);

        counter.Version = 2;
        await RunAsync(host);
        counter.Runs.ShouldBe(2);

        await RunAsync(host);
        counter.Runs.ShouldBe(2);
    }

    [Fact]
    public async Task ASeederThatFailsIsNotMarkedAndRunsAgainAtOnce()
    {
        var counter = new SeedCounter { FailNext = true };
        await using var host = Counting(counter, runOnStart: false);

        await Should.ThrowAsync<InvalidOperationException>(() => RunAsync(host));
        await RunAsync(host);

        counter.Runs.ShouldBe(1);
        counter.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task ASeederThatAnotherInstanceIsRunningIsSkippedUntilTheClaimRunsOut()
    {
        var counter = new SeedCounter();
        await using var host = Counting(counter, runOnStart: false);
        var now = host.Time.GetUtcNow();
        await ChangeSeedingStateAsync(host, settings => settings.SetDefaultAsync(new SeedingState
        {
            Claim = new SeedClaim { Id = Guid.NewGuid(), At = now },
        }, Token).AsTask());

        await RunAsync(host);
        counter.Runs.ShouldBe(0);

        host.Time.Advance(TimeSpan.FromMinutes(16));
        await RunAsync(host);
        counter.Runs.ShouldBe(1);
    }

    [Fact]
    public async Task OnlySeedersAddedToTheBuilderRun()
    {
        var counter = new SeedCounter();
        await using var host = new FluentCmsTestHost()
            .WithServices(services =>
            {
                services.AddSingleton(counter);
                services.AddTransient<ISeeder, CountingSeeder>();
            })
            .Configure(cms => cms.Seeding.RunOnStart = false);

        await RunAsync(host);

        counter.Attempts.ShouldBe(0);
    }

    [Fact]
    public void ALocaleWithoutASeederIsRefused()
    {
        using var host = new FluentCmsTestHost().Configure(cms => cms.Seeding.RequireLocale("fr"));

        var exception = Should.Throw<Exception>(() => host.Services);

        exception.ToString().ShouldContain(SeedingErrorCodes.LocaleWithoutSeeder);
    }

    [Fact]
    public async Task ASeederIsToldTheVersionItWasAppliedAt()
    {
        var counter = new SeedCounter();
        await using var host = Counting(counter, runOnStart: true);
        _ = host.Services;
        counter.LastApplied.ShouldBe(0);

        counter.Version = 2;
        await RunAsync(host);

        counter.LastApplied.ShouldBe(1);
    }

    [Fact]
    public async Task ALiveRunKeepsItsClaimByRenewingItSoANewerRunSkips()
    {
        var gate = new Gate();
        var counter = new SeedCounter();
        await using var host = Gated(gate, counter);
        _ = host.Services;

        var slow = Task.Run(() => RunAsync(host), Token);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);

        host.Time.Advance(TimeSpan.FromMilliseconds(500));
        await WaitForAsync(host, state => state.Claim?.At == host.Time.GetUtcNow());
        host.Time.Advance(TimeSpan.FromMilliseconds(500));

        await RunAsync(host);
        gate.Started.ShouldBe(1);
        counter.Attempts.ShouldBe(0);

        gate.Open.SetResult();
        await slow;
        counter.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task AllSeedersRunUnderOneClaimSoNoneStartsOnAnotherInstanceMeanwhile()
    {
        var gate = new Gate();
        var counter = new SeedCounter();
        await using var host = Gated(gate, counter);
        _ = host.Services;

        var slow = Task.Run(() => RunAsync(host), Token);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);

        await RunAsync(host);
        counter.Attempts.ShouldBe(0);

        gate.Open.SetResult();
        await slow;
        counter.Runs.ShouldBe(1);
        (await StateAsync(host)).Claim.ShouldBeNull();
    }

    [Fact]
    public async Task ARunWhoseClaimWasTakenOverStopsAndLeavesTheClaimAlone()
    {
        var gate = new Gate();
        var counter = new SeedCounter();
        await using var host = Gated(gate, counter);
        _ = host.Services;

        var slow = Task.Run(() => RunAsync(host), Token);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        var other = new SeedClaim { Id = Guid.NewGuid(), At = host.Time.GetUtcNow() };
        await MutateStateAsync(host, state => state.Claim = other);

        await Should.NotThrowAsync(() => slow);

        (await StateAsync(host)).Claim.ShouldNotBeNull().Id.ShouldBe(other.Id);
        counter.Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task ARunThatFailsAfterLosingItsClaimLeavesTheNewClaimAlone()
    {
        var gate = new Gate { FailOnOpen = true };
        var counter = new SeedCounter();
        await using var host = Gated(gate, counter, TimeSpan.FromMinutes(15));
        _ = host.Services;

        var slow = Task.Run(() => RunAsync(host), Token);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        var other = new SeedClaim { Id = Guid.NewGuid(), At = host.Time.GetUtcNow() };
        await MutateStateAsync(host, state => state.Claim = other);
        gate.Open.SetResult();
        await Record.ExceptionAsync(() => slow);

        (await StateAsync(host)).Claim.ShouldNotBeNull().Id.ShouldBe(other.Id);
    }

    [Fact]
    public async Task AVersionBumpDoesNotTrashAgainAnArticleAPersonRestored()
    {
        var box = new VersionBox();
        await using var host = Bumpable(box);
        var admin = await AdminOf(host);
        await RunAsync(host);
        var trashed = (await admin.Content<Article>().QueryTrashAsync(new ContentQuery { Status = QueryStatus.Draft }, Token)).Items.Single();
        await admin.Content<Article>().RestoreAsync(trashed.Id, Token);

        box.Version = 2;
        await RunAsync(host);

        (await admin.Content<Article>().QueryTrashAsync(new ContentQuery { Status = QueryStatus.Draft }, Token)).Items.ShouldBeEmpty();
        (await AllAsync<Article>(admin)).ShouldContain(article => article.Slug == BlogSeeder.TrashedSlug);
    }

    [Fact]
    public async Task AVersionBumpDoesNotMoveTheScheduleAndCreatesOnlyWhatIsMissing()
    {
        var box = new VersionBox();
        await using var host = Bumpable(box);
        var admin = await AdminOf(host);
        await RunAsync(host);
        var notes = (await AllAsync<Article>(admin)).Single(article => article.Slug == BlogSeeder.ScheduledSlug);
        Task<DateTimeOffset?> ScheduledAt() => admin.RunAsync<CmsDbContext, DateTimeOffset?>(context =>
            context.Documents.AsNoTracking().Where(row => row.DocumentId == notes.Id && row.Locale == "en").Select(row => row.SchedulePublishAt).SingleAsync());
        var before = await ScheduledAt();
        var tour = (await AllAsync<Article>(admin)).Single(article => article.Slug == BlogSeeder.TourSlug);
        await admin.Content<Article>().DeleteAsync(tour.Id, Token);
        await admin.Content<Article>().PurgeAsync(tour.Id, Token);
        var count = await DocumentsAsync(admin);

        host.Time.Advance(TimeSpan.FromDays(3));
        box.Version = 2;
        await RunAsync(host);

        (await ScheduledAt()).ShouldBe(before);
        (await DocumentsAsync(admin)).ShouldBe(count + 1);
        (await AllAsync<Article>(admin)).Single(article => article.Slug == BlogSeeder.TourSlug).Status.ShouldBe(EntryStatus.Draft);
    }

    [Fact]
    public async Task AFailureBeforeTheSeederRunsReleasesTheClaim()
    {
        var counter = new SeedCounter();
        await using var host = new FluentCmsTestHost()
            .WithServices(services => services.AddSingleton(counter))
            .Configure(cms =>
            {
                cms.Seeding.RequireLocale("not a locale!").Add<CountingSeeder>();
                cms.Seeding.RunOnStart = false;
            });

        await Should.ThrowAsync<ValidationException>(() => RunAsync(host));

        (await StateAsync(host)).Claim.ShouldBeNull();
        counter.Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task StateStepsOnEntriesThatAreGoneAreTreatedAsDone()
    {
        await using var host = new FluentCmsTestHost()
            .WithContentTypes([.. BlogSample.ContentTypes])
            .WithMediaDirectory()
            .WithServices(services => services.AddScoped(provider =>
                GoneOnStateSteps.Wrap(ActivatorUtilities.CreateInstance<TypedContentService<Article>>(provider))))
            .Configure(cms =>
            {
                cms.AddBlogSample();
                cms.Seeding.RunOnStart = false;
            });
        var admin = await AdminOf(host);

        await Should.NotThrowAsync(() => RunAsync(host));

        (await AllAsync<Article>(admin)).Count.ShouldBe(5);
        (await StateAsync(host)).Claim.ShouldBeNull();
        (await StateAsync(host)).Seeders.ShouldContainKey(typeof(BlogSeeder).FullName!);
    }

    // Stands in for an overlapping run that published, trashed or purged the entry between the seeder reading it and changing it.
    public class GoneOnStateSteps : DispatchProxy
    {
        private IContentService<Article> _inner = null!;

        public static IContentService<Article> Wrap(IContentService<Article> inner)
        {
            var proxy = Create<IContentService<Article>, GoneOnStateSteps>();
            ((GoneOnStateSteps)(object)proxy)._inner = inner;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name is "PublishAsync" or "DeleteAsync" or "ScheduleAsync")
            {
                throw new EntryNotFoundException("article", (Guid)args![0]!);
            }

            try
            {
                return targetMethod.Invoke(_inner, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
    }

    private static async Task<SeedingState> StateAsync(FluentCmsTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<SeedStateStore>().ReadAsync(Token)).State;
    }

    private static async Task MutateStateAsync(FluentCmsTestHost host, Action<SeedingState> change)
    {
        await using var scope = host.Services.CreateAsyncScope();
        using var operation = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Cli, Site);
        await scope.ServiceProvider.GetRequiredService<SeedStateStore>().MutateAsync(state =>
        {
            change(state);
            return true;
        }, Token);
    }

    private static async Task WaitForAsync(FluentCmsTestHost host, Func<SeedingState, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition(await StateAsync(host)))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The seeding state did not reach the expected shape.");
            }

            await Task.Delay(50, Token);
        }
    }

    private static FluentCmsTestHost Gated(Gate gate, SeedCounter counter, TimeSpan? claimTimeout = null) => new FluentCmsTestHost()
        .WithServices(services =>
        {
            services.AddSingleton(gate);
            services.AddSingleton(counter);
        })
        .Configure(cms =>
        {
            cms.Seeding.Add<GateSeeder>().Add<CountingSeeder>();
            cms.Seeding.RunOnStart = false;
            cms.Seeding.ClaimTimeout = claimTimeout ?? TimeSpan.FromMilliseconds(600);
        });

    private static FluentCmsTestHost Bumpable(VersionBox box) => new FluentCmsTestHost()
        .WithContentTypes([.. BlogSample.ContentTypes])
        .WithMediaDirectory()
        .WithServices(services => services.AddSingleton(box))
        .Configure(cms =>
        {
            cms.Settings.Add<BlogSettings>();
            cms.Seeding.RequireLocale(BlogSample.SecondLocale).Add<BumpableBlogSeeder>();
            cms.Seeding.RunOnStart = false;
        });

    private sealed class VersionBox
    {
        public int Version { get; set; } = 1;
    }

    private sealed class BumpableBlogSeeder(VersionBox box) : BlogSeeder
    {
        protected override int SeedVersion => box.Version;
    }

    private sealed class Gate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Open { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool FailOnOpen { get; set; }

        public int Started { get; set; }
    }

    private sealed class GateSeeder(Gate gate) : ISeeder
    {
        public async Task SeedAsync(SeedContext context, CancellationToken cancellationToken = default)
        {
            gate.Started++;
            gate.Entered.TrySetResult();
            await gate.Open.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            if (gate.FailOnOpen)
            {
                throw new InvalidOperationException("The seed failed.");
            }
        }
    }

    private static FluentCmsTestHost Counting(SeedCounter counter, bool runOnStart) => new FluentCmsTestHost()
        .WithServices(services => services.AddSingleton(counter))
        .Configure(cms =>
        {
            cms.Seeding.Add<CountingSeeder>();
            cms.Seeding.RunOnStart = runOnStart;
        });

    private sealed class SeedCounter
    {
        public int Version { get; set; } = 1;

        public int Runs { get; set; }

        public int Attempts { get; set; }

        public int LastApplied { get; set; } = -1;

        public bool FailNext { get; set; }
    }

    private sealed class CountingSeeder(SeedCounter counter) : ISeeder
    {
        public int Version => counter.Version;

        public Task SeedAsync(SeedContext context, CancellationToken cancellationToken = default)
        {
            counter.Attempts++;
            counter.LastApplied = context.AppliedVersion;
            if (counter.FailNext)
            {
                counter.FailNext = false;
                throw new InvalidOperationException("The seed failed.");
            }

            counter.Runs++;
            return Task.CompletedTask;
        }
    }
}
