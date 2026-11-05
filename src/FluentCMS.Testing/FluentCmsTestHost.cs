using System.Net.Http.Headers;
using FluentCMS.Content;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Sqlite;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Testing;

// Boots the CMS in-process on SQLite. Configure it first; the host starts on the first use of Services, a client or a
// fixture, and from then on configuring throws. It starts in the Development environment, so content tables come from
// the schema the way they do for a developer. It does not run as the system principal: a call runs as Anonymous until a
// test says otherwise with As, and only Seed runs as the system, with the Seed origin.
public sealed class FluentCmsTestHost : IAsyncDisposable, IDisposable
{
    private readonly List<Type> _contentTypes = [];
    private readonly List<Action<FluentCmsBuilder>> _configure = [];
    private readonly List<Action<IServiceCollection>> _services = [];
    private readonly List<SeedStep> _seeds = [];
    private readonly Lock _gate = new();

    private string? _file;
    private bool _inMemory = true;
    private bool _started;
    private bool _disposed;
    private SqliteConnection? _keeper;
    private DirectoryInfo? _directory;
    private IHost? _host;

    public ControllableTimeProvider Time { get; } = new();

    public IServiceProvider Services => Start().Services;

    public TestActor Anonymous => Act(Principal.Anonymous, Origin.Api, clientId: null);

    public FluentCmsTestHost UseSqlite()
    {
        EnsureNotStarted();
        _inMemory = true;
        _file = null;
        return this;
    }

    // A database file of its own. Without a path, a temporary one that is deleted with the host.
    public FluentCmsTestHost UseSqlite(string? file)
    {
        EnsureNotStarted();
        _inMemory = false;
        _file = file;
        return this;
    }

    public FluentCmsTestHost WithContentTypes<T>()
        where T : ContentItem => WithContentTypes(typeof(T));

    public FluentCmsTestHost WithContentTypes<T1, T2>()
        where T1 : ContentItem
        where T2 : ContentItem => WithContentTypes(typeof(T1), typeof(T2));

    public FluentCmsTestHost WithContentTypes<T1, T2, T3>()
        where T1 : ContentItem
        where T2 : ContentItem
        where T3 : ContentItem => WithContentTypes(typeof(T1), typeof(T2), typeof(T3));

    public FluentCmsTestHost WithContentTypes(params Type[] types)
    {
        EnsureNotStarted();
        _contentTypes.AddRange(types);
        return this;
    }

    public FluentCmsTestHost Configure(Action<FluentCmsBuilder> configure)
    {
        EnsureNotStarted();
        _configure.Add(configure);
        return this;
    }

    public FluentCmsTestHost WithServices(Action<IServiceCollection> configure)
    {
        EnsureNotStarted();
        _services.Add(configure);
        return this;
    }

    // Runs once the database is ready and before the host reports started, as the system principal with the Seed origin.
    public FluentCmsTestHost Seed(Func<IServiceProvider, Task> seed, SiteId? site = null, string? locale = null)
    {
        EnsureNotStarted();
        _seeds.Add(new SeedStep(seed, site ?? new SiteId(DefaultSite.Id), locale ?? DefaultSite.Locale));
        return this;
    }

    public FluentCmsTestHost Seed<T>(params T[] entries)
        where T : ContentItem => Seed(async services =>
        {
            var content = services.GetRequiredService<IContentService<T>>();
            foreach (var entry in entries)
            {
                await content.CreateAsync(entry);
            }
        });

    public TestActor As(TestUser user, Origin origin = Origin.Admin) => Act(user.Principal, origin, clientId: null);

    public TestActor As(TestApiKey key, Origin origin = Origin.Api) => Act(key.Principal, origin, key.Id.ToString());

    public IContentService<T> Content<T>()
        where T : ContentItem => Anonymous.Content<T>();

    public IContentService Content() => Anonymous.Content();

    public async Task<TestUser> CreateUserAsync(string role, string? name = null, CancellationToken cancellationToken = default) =>
        await Identities.CreateUserAsync(Services, role, name, cancellationToken);

    public async Task<TestApiKey> CreateApiKeyAsync(ApiKeyClass keyClass, string role, string? name = null, CancellationToken cancellationToken = default) =>
        await Identities.CreateApiKeyAsync(Services, keyClass, role, name, cancellationToken);

    // Each permission is a name or pattern as role grants hold them. The role is written the way role management will write it.
    public async Task<Guid> CreateRoleAsync(string name, IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        var role = new Role { Id = Guid.CreateVersion7(), Name = name };
        context.Set<Role>().Add(role);
        context.Set<RoleGrant>().AddRange(permissions.Select(permission => new RoleGrant { Id = Guid.CreateVersion7(), RoleId = role.Id, Permission = permission }));
        await context.SaveChangesAsync(cancellationToken);
        return role.Id;
    }

    public HttpClient AsKey(TestApiKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", key.Secret);
        return client;
    }

    // A user signs in through the authorization server, which arrives in M2.
    public HttpClient AsUser(TestUser user) =>
        throw new NotSupportedException("A user's HTTP client needs the token endpoint of the authorization server, which arrives in M2.");

    public HttpClient CreateClient() => Start().GetTestClient();

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        IHost? host;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            host = _host;
            _host = null;
        }

        if (host is not null)
        {
            await host.StopAsync();
            host.Dispose();
        }

        Release();
    }

    private IHost Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_host is not null)
            {
                return _host;
            }

            _started = true;
            var host = CreateHostBuilder().Build();
            try
            {
                host.Start();
            }
            catch
            {
                host.Dispose();
                Release();
                throw;
            }

            return _host = host;
        }
    }

    private IHostBuilder CreateHostBuilder()
    {
        var connectionString = OpenDatabase();
        var schema = BuildSchema();

        return new HostBuilder()
            .UseEnvironment(Environments.Development)
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            })
            .ConfigureWebHost(web => web.UseTestServer().Configure(Pipeline))
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(schema);
                services.AddSingleton<TimeProvider>(Time);
                foreach (var configure in _services)
                {
                    configure(services);
                }

                services.AddFluentCms(cms =>
                {
                    cms.UseSqlite(connectionString);
                    cms.UseContentMigrations(typeof(CmsDbContext).Assembly);
                    foreach (var configure in _configure)
                    {
                        configure(cms);
                    }
                });

                services.AddHostedService(provider => new SeedRunner(provider, Time, _seeds));
            });
    }

    // The pipeline a host's Program.cs builds, so that a client reaches the endpoints as a production client does.
    private static void Pipeline(IApplicationBuilder app)
    {
        app.UseFluentCms();
        app.UseEndpoints(endpoints => endpoints.MapFluentCms());
    }

    private TestActor Act(Principal principal, Origin origin, string? clientId) =>
        new(Services, Time, principal, origin, clientId, new SiteId(DefaultSite.Id), DefaultSite.Locale);

    private void EnsureNotStarted()
    {
        lock (_gate)
        {
            if (_started)
            {
                throw new InvalidOperationException("The test host has started; configure it before the first use of Services, a client or a fixture.");
            }
        }
    }

    private SchemaModel BuildSchema()
    {
        var builder = new SchemaBuilder();
        foreach (var type in _contentTypes)
        {
            builder.AddContentType(type);
        }

        return builder.Build();
    }

    private string OpenDatabase()
    {
        if (_inMemory)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"fluentcms-test-{Guid.NewGuid():N}",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
            }.ToString();
            _keeper = new SqliteConnection(connectionString);
            _keeper.Open();
            return connectionString;
        }

        var file = _file;
        if (file is null)
        {
            _directory = Directory.CreateTempSubdirectory("fluentcms-testing-");
            file = Path.Combine(_directory.FullName, "cms.db");
        }

        return new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString();
    }

    private void Release()
    {
        _keeper?.Dispose();
        _keeper = null;

        if (_directory is not null)
        {
            try
            {
                _directory.Delete(recursive: true);
            }
            catch (IOException)
            {
            }

            _directory = null;
        }
    }

    private sealed record SeedStep(Func<IServiceProvider, Task> Run, SiteId Site, string Locale);

    private sealed class SeedRunner(IServiceProvider provider, TimeProvider time, List<SeedStep> seeds) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (seeds.Count == 0)
            {
                return;
            }

            await using var scope = provider.CreateAsyncScope();
            var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();
            var previous = accessor.Current;

            try
            {
                foreach (var seed in seeds)
                {
                    accessor.Current = new OperationContext(
                        Principal.System,
                        Origin.Seed,
                        seed.Site,
                        seed.Locale,
                        ClientId: null,
                        Guid.CreateVersion7().ToString(),
                        time.GetUtcNow());
                    await seed.Run(scope.ServiceProvider);
                }
            }
            finally
            {
                accessor.Current = previous;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
