using System.Data.Common;
using System.Net;
using FluentCMS.Content;
using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// What a Delivery read sends to the database once the caches are warm (#2857), with the output cache off so that every
// request reaches the endpoint. The site, its locales, the Public role and an API key's row are kept or carried; the
// stamps of the caller's roles are read on every request, which is how a role change on another instance is seen on the
// next one.
public sealed class DeliveryRoundTripTests : IAsyncLifetime
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly Commands _commands = new();
    private readonly FluentCmsTestHost _host;
    private HttpClient _client = null!;
    private Note _note = null!;

    public DeliveryRoundTripTests()
    {
        _host = new FluentCmsTestHost()
            .WithContentTypes<Note, Topic>()
            .WithServices(services => services.PostConfigure<DeliveryCacheOptions>(cache => cache.OutputCache = false))
            .Configure(cms =>
            {
                cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(_commands));
                cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(_commands));
            });
    }

    public async ValueTask InitializeAsync()
    {
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<RoleGrant>().Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = DefaultSite.PublicRole, Permission = "content.note.read" });
            await context.SaveChangesAsync(Token);
        }

        var notes = _host.As(await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token)).Content<Note>();
        _note = await notes.CreateAsync(new Note { Title = "First", Slug = "first" }, Token);
        await notes.PublishAsync(_note.Id, cancellationToken: Token);
        _commands.Requests = _host.Services.GetRequiredService<IHttpContextAccessor>();
        _client = _host.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task AWarmAnonymousReadOfAPublicEntrySendsTheContentQueriesAndTheStampOfThePublicRole()
    {
        _host.Services.GetRequiredService<SiteCache>().Invalidate();
        var cold = await SentAsync(_client);
        var warm = await SentAsync(_client);

        // The stamp of the site's Public role, the entry with its document, and its references.
        cold.ShouldContain(sql => Reads(sql, "cms_site") || Reads(sql, "cms_site_locale"));
        warm.Count.ShouldBe(3, string.Join("\n\n", warm));
        warm.Count(sql => Reads(sql, "cms_role")).ShouldBe(1, string.Join("\n\n", warm));
        warm.Where(sql => !Reads(sql, "cms_role")).ShouldAllBe(sql => Reads(sql, "cms_document") || Reads(sql, "cms_reference"), string.Join("\n\n", warm));
    }

    // The handler reads the key once to check it; its role comes from that row, not from a second read.
    [Fact]
    public async Task AWarmReadWithAnApiKeyReadsTheKeyOnceAndTheStampOfItsRole()
    {
        var key = await _host.CreateApiKeyAsync(FluentCMS.Testing.ApiKeyClass.Delivery, BuiltInRoles.PublicName, cancellationToken: Token);
        using var client = _host.AsKey(key);
        await SentAsync(client);

        var warm = await SentAsync(client);

        warm.Count.ShouldBe(4, string.Join("\n\n", warm));
        warm.Count(sql => Reads(sql, "cms_api_key")).ShouldBe(1, string.Join("\n\n", warm));
        warm.Count(sql => Reads(sql, "cms_role")).ShouldBe(1, string.Join("\n\n", warm));
        warm.Where(sql => !Reads(sql, "cms_role") && !Reads(sql, "cms_api_key"))
            .ShouldAllBe(sql => Reads(sql, "cms_document") || Reads(sql, "cms_reference"), string.Join("\n\n", warm));
    }

    // The grant goes in a save of its own, as another instance's would, and the stamp it raises is read on the next request.
    [Fact]
    public async Task AChangeToThePublicRoleIsSeenOnTheNextRequest()
    {
        await SentAsync(_client);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.RemoveRange(await context.Set<RoleGrant>().Where(grant => grant.RoleId == DefaultSite.PublicRole).ToListAsync(Token));
            await context.SaveChangesAsync(Token);
        }

        using var refused = await _client.GetAsync(new Uri($"/api/delivery/v1/note/{_note.Id}", UriKind.Relative), Token);
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // A locale added through SiteManager is allowed from the next request; one written to the database by anything else, as
    // by another instance, once this instance's copy of the site's locales expires.
    [Fact]
    public async Task ALocaleChangeIsSeenAtOnceHereAndWithinTheCacheDurationWhenWrittenElsewhere()
    {
        (await StatusAsync("note?locale=fr")).ShouldBe(HttpStatusCode.UnprocessableEntity);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
            await scope.ServiceProvider.GetRequiredService<SiteManager>().AddLocaleAsync(DefaultSite.Id, "fr", null, true, null, Token);
        }

        (await StatusAsync("note?locale=fr")).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync("note?locale=de")).ShouldBe(HttpStatusCode.UnprocessableEntity);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Add(new SiteLocale { SiteId = DefaultSite.Id, Code = "de", Position = 5, Enabled = true });
            await context.SaveChangesAsync(Token);
        }

        (await StatusAsync("note?locale=de")).ShouldBe(HttpStatusCode.UnprocessableEntity);
        _host.Time.Advance(SiteCache.CacheDuration);
        (await StatusAsync("note?locale=de")).ShouldBe(HttpStatusCode.OK);
    }

    private async Task<HttpStatusCode> StatusAsync(string path)
    {
        using var response = await _client.GetAsync(new Uri("/api/delivery/v1/" + path, UriKind.Relative), Token);
        return response.StatusCode;
    }

    private static bool Reads(string sql, string table) => sql.Contains($"\"{table}\"", StringComparison.Ordinal);

    private async Task<List<string>> SentAsync(HttpClient client)
    {
        _commands.Arm();
        try
        {
            using var response = await client.GetAsync(new Uri($"/api/delivery/v1/note/{_note.Id}", UriKind.Relative), Token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        }
        finally
        {
            _commands.Disarm();
        }

        return _commands.Sent;
    }

    // Every command either context sends while armed for a request, and none of the job runner's, which has no request.
    private sealed class Commands : DbCommandInterceptor
    {
        private readonly List<string> _sent = [];
        private volatile bool _armed;

        public IHttpContextAccessor? Requests { get; set; }

        public List<string> Sent
        {
            get
            {
                lock (_sent)
                {
                    return [.. _sent];
                }
            }
        }

        public void Arm()
        {
            lock (_sent)
            {
                _sent.Clear();
            }

            _armed = true;
        }

        public void Disarm() => _armed = false;

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Add(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Add(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Add(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Add(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Add(command);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Add(command);
            return ValueTask.FromResult(result);
        }

        private void Add(DbCommand command)
        {
            if (_armed && Requests?.HttpContext is not null)
            {
                lock (_sent)
                {
                    _sent.Add(command.CommandText);
                }
            }
        }
    }
}
