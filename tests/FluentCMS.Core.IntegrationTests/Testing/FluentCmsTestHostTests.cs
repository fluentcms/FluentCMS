using System.Diagnostics;
using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Testing;

// These tests use Note, a collection of their own, so that they do not depend on the sample types of the blog.
public sealed class FluentCmsTestHostTests : IAsyncDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly FluentCmsTestHost _host = new FluentCmsTestHost().WithContentTypes<Note>();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task ABootSeedsTheTypesAndQueriesThemInUnderTwoSeconds()
    {
        // The first run of a process pays for JIT and for building the EF models, about 1.8 s on its own. The bound is on the best
        // of a few runs after it, so that neither the first run nor a busy shared runner fails the test, while a boot an order of
        // magnitude slower still does.
        await BootAsync();
        List<TimeSpan> boots = [];
        for (var run = 0; run < 3; run++)
        {
            boots.Add(await BootAsync());
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Warm boots took {string.Join(", ", boots.Select(boot => $"{boot.TotalMilliseconds:F0}"))} ms");
        boots.Min().ShouldBeLessThan(TimeSpan.FromSeconds(2));

        static async Task<TimeSpan> BootAsync()
        {
            var clock = Stopwatch.StartNew();
            await using var host = new FluentCmsTestHost()
                .WithContentTypes<Note>()
                .Seed(new Note { Title = "First" }, new Note { Title = "Second" });
            var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);

            var result = await host.As(editor).Content<Note>().QueryAsync(new ContentQuery { Status = QueryStatus.Draft }, Token);
            clock.Stop();

            result.Items.Select(note => note.Title).Order().ToArray().ShouldBe(["First", "Second"]);
            return clock.Elapsed;
        }
    }

    [Fact]
    public async Task ACallRunsAsAnonymousUntilATestSaysOtherwise()
    {
        var accessorSeen = await _host.Anonymous.RunAsync<IOperationContextAccessor, OperationContext?>(accessor => Task.FromResult(accessor.Current));

        accessorSeen.ShouldNotBeNull();
        accessorSeen.Principal.ShouldBe(Principal.Anonymous);
        accessorSeen.Principal.IsSystem.ShouldBeFalse();
        accessorSeen.Origin.ShouldBe(Origin.Api);

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _host.Content<Note>().CreateAsync(new Note { Title = "Nope" }, Token));
    }

    [Fact]
    public async Task TheOperationContextIsSetPerCallAndPutBack()
    {
        var accessor = _host.Services.GetRequiredService<IOperationContextAccessor>();
        var key = await _host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: Token);

        var seen = await _host.As(key).RunAsync<IOperationContextAccessor, OperationContext?>(inner => Task.FromResult(inner.Current));

        seen.ShouldNotBeNull();
        seen.Principal.ShouldBe(key.Principal);
        seen.ClientId.ShouldBe(key.Id.ToString());
        seen.Origin.ShouldBe(Origin.Api);
        accessor.Current.ShouldBeNull();
    }

    [Fact]
    public async Task SeedRunsAsTheSystemWithTheSeedOriginAndNothingAfterIt()
    {
        OperationContext? during = null;
        await using var host = new FluentCmsTestHost()
            .WithContentTypes<Note>()
            .Seed(services =>
            {
                during = services.GetRequiredService<IOperationContextAccessor>().Current;
                return Task.CompletedTask;
            });

        _ = host.Services;

        during.ShouldNotBeNull();
        during.Principal.IsSystem.ShouldBeTrue();
        during.Origin.ShouldBe(Origin.Seed);
        host.Services.GetRequiredService<IOperationContextAccessor>().Current.ShouldBeNull();
        await Should.ThrowAsync<ContentAccessDeniedException>(() => host.Content<Note>().CreateAsync(new Note { Title = "Nope" }, Token));
    }

    [Theory]
    [InlineData(BuiltInRoles.SuperAdminName)]
    [InlineData(BuiltInRoles.EditorName)]
    [InlineData(BuiltInRoles.AuthorName)]
    public async Task AUserHoldsARealRoleAndWritesAsThemself(string role)
    {
        var user = await _host.CreateUserAsync(role, cancellationToken: Token);

        var created = await _host.As(user).Content<Note>().CreateAsync(new Note { Title = "Mine" }, Token);

        user.Principal.Kind.ShouldBe(PrincipalKind.AdminUser);
        user.Principal.Claim(ClaimPrincipalRoles.Claim).ShouldBeNull();
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            (await users.GetRoleIdsAsync((await users.FindByIdAsync(user.Id.ToString()))!)).ShouldBe([user.RoleId]);
        }

        created.CreatedBy.ShouldBe(user.Id);
        created.UpdatedBy.ShouldBe(user.Id);
    }

    [Fact]
    public async Task AUserWithoutGrantsIsRefused()
    {
        var member = await _host.CreateUserAsync(BuiltInRoles.MemberName, cancellationToken: Token);

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _host.As(member).Content<Note>().CreateAsync(new Note { Title = "Nope" }, Token));
    }

    [Fact]
    public async Task ARoleThatDoesNotExistIsRefused()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(() => _host.CreateUserAsync("Wizard", cancellationToken: Token));

        exception.Message.ShouldContain("Wizard");
        await Should.ThrowAsync<InvalidOperationException>(() => _host.CreateApiKeyAsync(ApiKeyClass.Delivery, "Wizard", cancellationToken: Token));
    }

    [Fact]
    public async Task ACustomRoleGrantsExactlyWhatItIsGiven()
    {
        var role = await _host.CreateRoleAsync("Note reader", ["content.note.read"], Token);
        var user = await _host.CreateUserAsync("Note reader", cancellationToken: Token);
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await _host.As(editor).Content<Note>().CreateAsync(new Note { Title = "Seen" }, Token);

        user.RoleId.ShouldBe(role);
        var read = await _host.As(user).Content<Note>().QueryAsync(new ContentQuery { Status = QueryStatus.Draft }, Token);
        read.Items.Count.ShouldBe(1);
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _host.As(user).Content<Note>().CreateAsync(new Note { Title = "Nope" }, Token));
    }

    [Theory]
    [InlineData(ApiKeyClass.Delivery, "fcms_dk_", TokenScopes.Delivery)]
    [InlineData(ApiKeyClass.Preview, "fcms_pk_", TokenScopes.Preview)]
    [InlineData(ApiKeyClass.Management, "fcms_mk_", TokenScopes.Management)]
    public async Task AKeyOfEachClassCarriesItsFormatAndScope(ApiKeyClass keyClass, string prefix, string scope)
    {
        var key = await _host.CreateApiKeyAsync(keyClass, BuiltInRoles.EditorName, cancellationToken: Token);

        key.Secret.ShouldStartWith(prefix);
        key.Secret.Length.ShouldBe(prefix.Length + 43);
        key.Principal.Kind.ShouldBe(PrincipalKind.ApiKey);
        key.Principal.Claim(TokenScopes.Claim).ShouldBe(scope);
        key.Principal.Claim(ClaimPrincipalRoles.Claim).ShouldBe(key.RoleId.ToString());
    }

    [Fact]
    public async Task TheClassOfAKeyDecidesWhatItsRoleCanDo()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await _host.As(editor).Content<Note>().CreateAsync(new Note { Title = "Draft" }, Token);
        var delivery = await _host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.EditorName, cancellationToken: Token);
        var preview = await _host.CreateApiKeyAsync(ApiKeyClass.Preview, BuiltInRoles.EditorName, cancellationToken: Token);
        var management = await _host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: Token);
        var drafts = new ContentQuery { Status = QueryStatus.Draft };

        (await _host.As(preview).Content<Note>().QueryAsync(drafts, Token)).Items.Count.ShouldBe(1);
        (await _host.As(management).Content<Note>().QueryAsync(drafts, Token)).Items.Count.ShouldBe(1);
        await Should.ThrowAsync<ValidationException>(() => _host.As(delivery).Content<Note>().QueryAsync(drafts, Token));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _host.As(delivery).Content<Note>().CreateAsync(new Note { Title = "Nope" }, Token));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _host.As(preview).Content<Note>().CreateAsync(new Note { Title = "Nope" }, Token));
        (await _host.As(management).Content<Note>().CreateAsync(new Note { Title = "Yes" }, Token)).CreatedBy.ShouldBe(management.Id);
    }

    [Fact]
    public async Task TheClockOfTheHostStampsWhatItWrites()
    {
        var moment = new DateTimeOffset(2030, 5, 6, 7, 8, 9, TimeSpan.Zero);
        _host.Time.Set(moment);
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);

        var created = await _host.As(editor).Content<Note>().CreateAsync(new Note { Title = "Dated" }, Token);
        _host.Time.Advance(TimeSpan.FromHours(1));
        var later = await _host.As(editor).Content<Note>().CreateAsync(new Note { Title = "Later" }, Token);

        created.CreatedAt.ShouldBe(moment);
        later.CreatedAt.ShouldBe(moment.AddHours(1));
    }

    [Fact]
    public async Task AKeyClientSendsTheKeyAsAProductionClientDoes()
    {
        var key = await _host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.EditorName, cancellationToken: Token);

        using var client = _host.AsKey(key);

        client.DefaultRequestHeaders.Authorization.ShouldNotBeNull();
        client.DefaultRequestHeaders.Authorization.Scheme.ShouldBe("ApiKey");
        client.DefaultRequestHeaders.Authorization.Parameter.ShouldBe(key.Secret);
    }

    [Fact]
    public async Task AUserClientSendsTheBearerTokenOfTheCodeFlow()
    {
        var user = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);

        using var client = await _host.AsUserAsync(user);

        client.DefaultRequestHeaders.Authorization.ShouldNotBeNull();
        client.DefaultRequestHeaders.Authorization.Scheme.ShouldBe("Bearer");
        client.DefaultRequestHeaders.Authorization.Parameter!.Split('.').Length.ShouldBe(3);
    }

    [Fact]
    public void ConfiguringAStartedHostThrows()
    {
        _ = _host.Services;

        Should.Throw<InvalidOperationException>(() => _host.WithContentTypes<Note>());
        Should.Throw<InvalidOperationException>(() => _host.Seed(_ => Task.CompletedTask));
    }

    [Fact]
    public async Task ATemporaryFileDatabaseIsRemovedWithTheHost()
    {
        var host = new FluentCmsTestHost().UseSqlite(file: null).WithContentTypes<Note>();
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await host.As(editor).Content<Note>().CreateAsync(new Note { Title = "On disk" }, Token);
        string file;
        using (var scope = host.Services.CreateScope())
        {
            file = scope.ServiceProvider.GetRequiredService<CmsDbContext>().Database.GetDbConnection().DataSource;
        }

        File.Exists(file).ShouldBeTrue();

        await host.DisposeAsync();

        Directory.Exists(Path.GetDirectoryName(file)).ShouldBeFalse();
    }

    [Fact]
    public async Task AFilePathWithASemicolonIsUsedAsGiven()
    {
        var directory = Directory.CreateTempSubdirectory("fluentcms-testing-semicolon-");
        var file = Path.Combine(directory.FullName, "a;b.db");
        try
        {
            await using (var host = new FluentCmsTestHost().UseSqlite(file).WithContentTypes<Note>())
            {
                var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
                await host.As(editor).Content<Note>().CreateAsync(new Note { Title = "Kept" }, Token);
            }

            File.Exists(file).ShouldBeTrue();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ADisposedHostDoesNotRestartAndDisposesTwice()
    {
        var host = new FluentCmsTestHost().WithContentTypes<Note>();
        _ = host.Services;

        await host.DisposeAsync();
        await host.DisposeAsync();
        host.Dispose();

        Should.Throw<ObjectDisposedException>(() => host.Services);
        Should.Throw<ObjectDisposedException>(() => host.CreateClient());
        await Should.ThrowAsync<ObjectDisposedException>(() => host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
    }

    [Fact]
    public void ACaptiveDependencyFailsTheBootAsItDoesInADevelopersHost()
    {
        using var host = new FluentCmsTestHost()
            .WithServices(services =>
            {
                services.AddScoped<ScopedProbe>();
                services.AddSingleton<CaptiveProbe>();
            });

        Should.Throw<Exception>(() => host.Services).ToString().ShouldContain(nameof(ScopedProbe));
    }

    [Fact]
    public async Task AnActorCanBeMovedToAnotherLocaleAndSite()
    {
        var site = new SiteId(Guid.CreateVersion7());
        var user = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var actor = _host.As(user);

        var moved = actor.In("fr").In(site);
        var seen = await moved.RunAsync<IOperationContextAccessor, OperationContext?>(accessor => Task.FromResult(accessor.Current));

        seen.ShouldNotBeNull();
        seen.Locale.ShouldBe("fr");
        seen.Site.ShouldBe(site);
        seen.Principal.ShouldBe(user.Principal);
        actor.Locale.ShouldBe(DefaultSite.Locale);
        actor.Site.ShouldBe(new SiteId(DefaultSite.Id));
    }

    [Fact]
    public async Task ASeedCanChooseItsSiteAndLocale()
    {
        var site = new SiteId(Guid.CreateVersion7());
        OperationContext? seen = null;
        await using var host = new FluentCmsTestHost()
            .WithContentTypes<Note>()
            .Seed(
                services =>
                {
                    seen = services.GetRequiredService<IOperationContextAccessor>().Current;
                    return Task.CompletedTask;
                },
                site,
                "de");

        _ = host.Services;

        seen.ShouldNotBeNull();
        seen.Site.ShouldBe(site);
        seen.Locale.ShouldBe("de");
        seen.Principal.IsSystem.ShouldBeTrue();
    }

    [Fact]
    public async Task AWriteLeavesARowInTheActivityLogWithoutAnyoneAskingForIt()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var actor = _host.As(editor);

        await actor.Content<Note>().CreateAsync(new Note { Title = "Logged" }, Token);

        IReadOnlyList<ActivityItem> items = [];
        for (var attempt = 0; attempt < 50 && items.Count == 0; attempt++)
        {
            await _host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token);
            items = (await actor.RunAsync<IActivityQuery, PagedResult<ActivityItem>>(query => query.QueryAsync(null, null, Token))).Items;
            if (items.Count == 0)
            {
                await Task.Delay(100, Token);
            }
        }

        var item = items.ShouldHaveSingleItem();
        item.Action.ShouldBe("content.draft_saved");
        item.ActorId.ShouldBe(editor.Id);
        item.Origin.ShouldBe(Origin.Admin);
    }

    private sealed class ScopedProbe;

    private sealed class CaptiveProbe(ScopedProbe scoped)
    {
        public ScopedProbe Scoped { get; } = scoped;
    }
}
