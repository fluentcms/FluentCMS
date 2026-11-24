using System.Buffers.Text;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentCMS.Activity;
using FluentCMS.Agents;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Caching;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Providers;
using FluentCMS.Settings;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;
using Shouldly;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The host the tests of the MCP clients share. Every test signs in several times, and the buckets refill only when the
// test clock moves, so the auth limit is out of the way; the limit of registrations per address is the tests' own.
public sealed class McpClientFixture : IAsyncDisposable
{
    public FluentCmsTestHost Host { get; } = McpTesting.Host()
        .WithServices(services => services.PostConfigure<RateLimitOptions>(limits => limits.Auth.PermitLimit = 10_000));

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}

// The host of the tests that cross two changes of a client: the same host, with the application manager of OpenIddict
// behind hooks that a test arms for one call.
public sealed class McpClientHookFixture : IAsyncDisposable
{
    public McpClientHookFixture()
    {
        Host = McpTesting.Host().WithServices(services =>
        {
            services.PostConfigure<RateLimitOptions>(limits => limits.Auth.PermitLimit = 10_000);
            // The manager of the interface is resolved by its closed type, which comes before OpenIddict's open registration.
            services.AddSingleton(Hooks);
            services.AddSingleton(Consents);
            services.AddScoped<OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication<Guid>>, HookedApplicationManager>();
            services.AddScoped<OpenIddictAuthorizationManager<OpenIddictEntityFrameworkCoreAuthorization<Guid>>, HookedAuthorizationManager>();
            services.AddSingleton<IDistributedLock>(provider => new ArrivalsLock(ActivatorUtilities.CreateInstance<DatabaseDistributedLock>(provider), Consents));
            services.AddSingleton<ILoggerProvider>(Logs);
        }).Configure(cms => cms.Events.AddHandler<UserSessionsEndedEvent, SessionsEndedHook>());
    }

    public FluentCmsTestHost Host { get; }

    public ApplicationManagerHooks Hooks { get; } = new();

    public ConsentGate Consents { get; } = new();

    internal LogCapture Logs { get; } = new();

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}

// What runs, once, in place of the next call of the application manager: work before the next read of a client's
// properties, which then answers what it read before the work, as a request that read them just before a change does;
// a refusal of the next update, as when another change of the application got there first; or a refusal of the next
// delete, by the database, as when a consent to the application arrived while it was deleted, or because the
// application changed since it was read.
public sealed class ApplicationManagerHooks
{
    private Func<Task>? _beforeRead;
    private int _failUpdate;
    private int _failDelete;
    private int _counting;
    private int _finds;

    public void BeforeNextPropertiesRead(Func<Task> work) => _beforeRead = work;

    public void FailNextUpdate() => _failUpdate = 1;

    private Guid? _failDeleteOf;

    // The next delete, or the next delete of that application only, so that clients other tests of the host left behind
    // do not take the failure.
    public void FailNextDelete(DeleteFailure failure = DeleteFailure.ForeignKey, Guid? of = null)
    {
        _failDeleteOf = of;
        _failDelete = (int)failure;
    }

    // Counts the reads of an application by its id from now on, as each client a bulk removal reads again.
    public void CountFinds()
    {
        Interlocked.Exchange(ref _finds, 0);
        Interlocked.Exchange(ref _counting, 1);
    }

    public int StopCounting()
    {
        Interlocked.Exchange(ref _counting, 0);
        return Volatile.Read(ref _finds);
    }

    internal Func<Task>? TakeBeforeRead() => Interlocked.Exchange(ref _beforeRead, null);

    internal bool TakeFailUpdate() => Interlocked.Exchange(ref _failUpdate, 0) == 1;

    internal DeleteFailure TakeFailDelete(Guid application) =>
        _failDeleteOf is { } of && of != application ? DeleteFailure.None : (DeleteFailure)Interlocked.Exchange(ref _failDelete, 0);

    internal void Found()
    {
        if (Volatile.Read(ref _counting) == 1)
        {
            Interlocked.Increment(ref _finds);
        }
    }

    private Func<Task>? _beforeUpdate;
    private Guid _beforeUpdateOf;
    private int _failAuthorizationDelete;

    // Work just before the next update of that application is saved, after the caller read what it writes.
    public void BeforeNextUpdateOf(Guid application, Func<Task> work)
    {
        _beforeUpdateOf = application;
        _beforeUpdate = work;
    }

    // A refusal of a delete of an authorization by its foreign key, as when a token of it was issued meanwhile: the next
    // one, or the one after as many as passed go through.
    public void FailNextAuthorizationDelete(int passed = 0) => _failAuthorizationDelete = passed + 1;

    // Work once the next end of a session has committed, as UserSessionsEndedEvent is heard after its commit.
    public void AfterNextSessionsEnded(Func<Task> work) => _afterSessionsEnded = work;

    private Func<Task>? _afterSessionsEnded;

    internal Func<Task>? TakeBeforeUpdate(Guid application) =>
        _beforeUpdateOf == application ? Interlocked.Exchange(ref _beforeUpdate, null) : null;

    internal bool TakeFailAuthorizationDelete()
    {
        var left = Volatile.Read(ref _failAuthorizationDelete);
        return left > 0 && Interlocked.Decrement(ref _failAuthorizationDelete) == 0;
    }

    internal Func<Task>? TakeAfterSessionsEnded() => Interlocked.Exchange(ref _afterSessionsEnded, null);
}

internal sealed class SessionsEndedHook(ApplicationManagerHooks hooks) : IEventHandler<UserSessionsEndedEvent>
{
    public async ValueTask HandleAsync(UserSessionsEndedEvent message, CancellationToken cancellationToken)
    {
        if (hooks.TakeAfterSessionsEnded() is { } work)
        {
            await work();
        }
    }
}

public enum DeleteFailure
{
    None = 0,

    // The foreign key of a consent or a token that arrived during the delete refuses it, as SQLite says so.
    ForeignKey = 1,

    // The application changed since it was read.
    ChangedMeanwhile = 2,

    // Another constraint refuses the save, as a CHECK refuses a damaged write of the outbox.
    OtherConstraint = 3,
}

// Holds every consent that takes a place at its insert until the test lets them go, and says when a consent reaches the
// insert or the lock of the consents, so that a test makes two consents race at the one moment the cap is read.
public sealed class ConsentGate
{
    private TaskCompletionSource _released = Done();
    private TaskCompletionSource _secondInsert = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _firstInsert = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _secondLock = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _inserts;
    private int _locks;
    private int _armed;

    public Task FirstInsert => _firstInsert.Task;

    // The second consent reached the insert: it read the cap while the first had not written.
    public Task SecondInsert => _secondInsert.Task;

    // The second consent waits for the lock while the first holds it.
    public Task SecondLock => _secondLock.Task;

    public void Arm()
    {
        _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _firstInsert = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _secondInsert = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _secondLock = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _inserts, 0);
        Interlocked.Exchange(ref _locks, 0);
        Interlocked.Exchange(ref _armed, 1);
    }

    public void Release()
    {
        Interlocked.Exchange(ref _armed, 0);
        Interlocked.Exchange(ref _refused, 0);
        _released.TrySetResult();
    }

    internal Task AtInsertAsync()
    {
        if (Volatile.Read(ref _armed) == 0)
        {
            return Task.CompletedTask;
        }

        (Interlocked.Increment(ref _inserts) == 1 ? _firstInsert : _secondInsert).TrySetResult();
        return _released.Task;
    }

    // How many times the lock of the consents was asked for since Arm.
    public int Locks => Volatile.Read(ref _locks);

    // The lock of the consents is answered as not free, as it is once its wait has run out, until Release.
    public void RefuseLock() => Interlocked.Exchange(ref _refused, 1);

    private int _refused;

    // False when the lock is to be answered as not free.
    internal bool AtLock(string name)
    {
        if (name != ClientConsents.LockName)
        {
            return true;
        }

        if (Volatile.Read(ref _armed) == 1 && Interlocked.Increment(ref _locks) == 2)
        {
            _secondLock.TrySetResult();
        }

        return Volatile.Read(ref _refused) == 0;
    }

    private static TaskCompletionSource Done()
    {
        var done = new TaskCompletionSource();
        done.SetResult();
        return done;
    }
}

internal sealed class ArrivalsLock(IDistributedLock inner, ConsentGate gate) : IDistributedLock
{
    public Task<IDistributedLockHandle?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        gate.AtLock(name) ? inner.TryAcquireAsync(name, timeout, cancellationToken) : Task.FromResult<IDistributedLockHandle?>(null);
}

internal sealed class HookedAuthorizationManager(
    IOpenIddictAuthorizationCache<OpenIddictEntityFrameworkCoreAuthorization<Guid>> cache,
    ILogger<OpenIddictAuthorizationManager<OpenIddictEntityFrameworkCoreAuthorization<Guid>>> logger,
    IOptionsMonitor<OpenIddictCoreOptions> options,
    IOpenIddictAuthorizationStore<OpenIddictEntityFrameworkCoreAuthorization<Guid>> store,
    ConsentGate gate,
    ApplicationManagerHooks hooks) : OpenIddictAuthorizationManager<OpenIddictEntityFrameworkCoreAuthorization<Guid>>(cache, logger, options, store)
{
    public override async ValueTask CreateAsync(OpenIddictEntityFrameworkCoreAuthorization<Guid> authorization, CancellationToken cancellationToken = default)
    {
        await gate.AtInsertAsync();
        await base.CreateAsync(authorization, cancellationToken);
    }

    public override ValueTask DeleteAsync(OpenIddictEntityFrameworkCoreAuthorization<Guid> authorization, CancellationToken cancellationToken = default) =>
        hooks.TakeFailAuthorizationDelete()
            ? ValueTask.FromException(new DbUpdateException("The delete crossed a token issued under the authorization.", new SqliteException("SQLite Error 19: 'FOREIGN KEY constraint failed'.", 19, 787)))
            : base.DeleteAsync(authorization, cancellationToken);
}

internal sealed class HookedApplicationManager(
    IOpenIddictApplicationCache<OpenIddictEntityFrameworkCoreApplication<Guid>> cache,
    ILogger<OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication<Guid>>> logger,
    IOptionsMonitor<OpenIddictCoreOptions> options,
    IOpenIddictApplicationStore<OpenIddictEntityFrameworkCoreApplication<Guid>> store,
    ApplicationManagerHooks hooks,
    SystemDbContext context) : OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication<Guid>>(cache, logger, options, store)
{
    // The store marks the application deleted before it saves, and a save the database refuses leaves it so. The
    // refusals carry the errors SQLite gives, with their extended codes.
    public override ValueTask DeleteAsync(OpenIddictEntityFrameworkCoreApplication<Guid> application, CancellationToken cancellationToken = default)
    {
        switch (hooks.TakeFailDelete(application.Id))
        {
            case DeleteFailure.None:
                return base.DeleteAsync(application, cancellationToken);
            case DeleteFailure.ChangedMeanwhile:
                return ValueTask.FromException(new OpenIddictExceptions.ConcurrencyException("The application was changed by another request."));
            case DeleteFailure.OtherConstraint:
                context.Remove(application);
                return ValueTask.FromException(new DbUpdateException("The save was refused.", new SqliteException("SQLite Error 19: 'CHECK constraint failed'.", 19, 275)));
            default:
                context.Remove(application);
                return ValueTask.FromException(new DbUpdateException("The delete crossed an insert that refers to the application.", new SqliteException("SQLite Error 19: 'FOREIGN KEY constraint failed'.", 19, 787)));
        }
    }

    public override ValueTask<OpenIddictEntityFrameworkCoreApplication<Guid>?> FindByIdAsync(string identifier, CancellationToken cancellationToken = default)
    {
        hooks.Found();
        return base.FindByIdAsync(identifier, cancellationToken);
    }

    public override async ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OpenIddictEntityFrameworkCoreApplication<Guid> application, CancellationToken cancellationToken = default)
    {
        var properties = await base.GetPropertiesAsync(application, cancellationToken);
        if (hooks.TakeBeforeRead() is { } work)
        {
            await work();
        }

        return properties;
    }

    // Every update of an application ends here, the one with a descriptor included.
    public override async ValueTask UpdateAsync(OpenIddictEntityFrameworkCoreApplication<Guid> application, CancellationToken cancellationToken = default)
    {
        if (hooks.TakeBeforeUpdate(application.Id) is { } work)
        {
            await work();
        }

        if (hooks.TakeFailUpdate())
        {
            throw new OpenIddictExceptions.ConcurrencyException("The application was changed by another request.");
        }

        await base.UpdateAsync(application, cancellationToken);
    }
}

// What an authorization request came to: a code at the client's redirect URI, the error the client or the error page
// was given, or the hosted page the flow stopped at.
internal sealed record Authorized(string? Code, string? Error, string? StoppedAt, string Verifier);

// An MCP client and the browser of its user: it comes from one client address, keeps cookies, registers itself, sends
// the user through the authorization code flow with PKCE, the consent page included, and redeems the code.
internal sealed partial class McpAgent : IDisposable
{
    // Where a native MCP client listens, which no test ever calls.
    public const string RedirectUri = "http://127.0.0.1:33418/callback";

    public const string Scopes = "openid mcp offline_access";

    private static readonly Uri Base = new("https://localhost/");

    private readonly CookieContainer _cookies = new();
    private readonly FluentCmsTestHost _host;
    private readonly HttpMessageHandler _server;
    private readonly HttpClient _client;

    public McpAgent(FluentCmsTestHost host, string address)
    {
        _host = host;
        _server = ((TestServer)host.Services.GetRequiredService<IServer>()).CreateHandler(context => context.Connection.RemoteIpAddress = IPAddress.Parse(address));
        _client = new HttpClient(new CookieHandler(_cookies, _server), disposeHandler: false) { BaseAddress = Base };
    }

    public HttpClient Http => _client;

    public static string Registration(string name = "Test agent", string redirectUri = RedirectUri) =>
        $$"""{ "client_name": "{{name}}", "redirect_uris": ["{{redirectUri}}"], "grant_types": ["authorization_code", "refresh_token"], "response_types": ["code"], "token_endpoint_auth_method": "none" }""";

    public async Task<HttpResponseMessage> RegisterAsync(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _client.PostAsync(new Uri("/connect/register", UriKind.Relative), content, Token);
    }

    // The client id of a registration that must succeed.
    public async Task<string> RegisteredAsync(string name = "Test agent")
    {
        using var response = await RegisterAsync(Registration(name));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        return (await JsonAsync(response))["client_id"]!.GetValue<string>();
    }

    public async Task SignInAsync(TestUser user) => _cookies.SetCookies(Base, await _host.SignInCookieAsync(user));

    public async Task<Authorized> AuthorizeAsync(string clientId, string scope = Scopes, bool allow = true, string redirectUri = RedirectUri)
    {
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var next = "/connect/authorize" + QueryString.Create(new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["scope"] = scope,
            ["code_challenge"] = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256",
            ["state"] = "test",
            ["resource"] = "https://localhost/mcp",
        });

        for (var hops = 0; hops < 10; hops++)
        {
            var path = next.Split('?')[0];
            if (path == "/account/error")
            {
                return new Authorized(null, QueryHelpers.ParseQuery(next[(next.IndexOf('?', StringComparison.Ordinal) + 1)..])["error"].ToString(), path, verifier);
            }

            if (path is not ("/connect/authorize" or "/account/consent"))
            {
                return new Authorized(null, null, path, verifier);
            }

            using var response = path == "/account/consent"
                ? await ConsentAsync(next, allow)
                : await _client.GetAsync(new Uri(next, UriKind.Relative), Token);
            if (response.StatusCode != HttpStatusCode.Redirect || response.Headers.Location is not { } location)
            {
                throw new ShouldAssertException($"{next} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Token)}");
            }

            if (location.IsAbsoluteUri && location.AbsoluteUri.StartsWith(redirectUri, StringComparison.Ordinal))
            {
                var returned = QueryHelpers.ParseQuery(location.Query);
                return new Authorized(returned.TryGetValue("code", out var code) ? code.ToString() : null, returned.TryGetValue("error", out var error) ? error.ToString() : null, null, verifier);
            }

            next = location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
        }

        throw new ShouldAssertException("Too many redirects.");
    }

    // Opens the consent page and posts its form, as its buttons do. A page that does not show answers for itself.
    public async Task<HttpResponseMessage> ConsentAsync(string consentUrl, bool allow)
    {
        var page = await _client.GetAsync(new Uri(consentUrl, UriKind.Relative), Token);
        if (page.StatusCode != HttpStatusCode.OK)
        {
            return page;
        }

        using var shown = page;
        var match = Antiforgery().Match(await page.Content.ReadAsStringAsync(Token));
        match.Success.ShouldBeTrue("The consent page has no antiforgery token.");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["decision"] = allow ? "allow" : "deny",
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value),
        });
        return await _client.PostAsync(new Uri(consentUrl, UriKind.Relative), form, Token);
    }

    public async Task<HttpResponseMessage> RedeemAsync(string clientId, Authorized authorized, string redirectUri = RedirectUri)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorized.Code.ShouldNotBeNull(),
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = authorized.Verifier,
            ["resource"] = "https://localhost/mcp",
        });
        return await _client.PostAsync(new Uri("/connect/token", UriKind.Relative), form, Token);
    }

    public async Task<HttpResponseMessage> RefreshAsync(string clientId, string refreshToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        });
        return await _client.PostAsync(new Uri("/connect/token", UriKind.Relative), form, Token);
    }

    // The whole flow for a client that is approved: the tokens of the user through the client.
    public async Task<JsonNode> TokensAsync(string clientId, TestUser user, string scope = Scopes)
    {
        await SignInAsync(user);
        var authorized = await AuthorizeAsync(clientId, scope);
        authorized.Code.ShouldNotBeNull($"The flow ended with {authorized.Error} at {authorized.StoppedAt}.");
        using var redeemed = await RedeemAsync(clientId, authorized);
        redeemed.StatusCode.ShouldBe(HttpStatusCode.OK, await redeemed.Content.ReadAsStringAsync(Token));
        return await JsonAsync(redeemed);
    }

    // A client of /mcp with a bearer token, from the agent's address.
    public HttpClient Bearer(string accessToken)
    {
        var client = new HttpClient(_server, disposeHandler: false) { BaseAddress = Base };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex Antiforgery();

    private sealed class CookieHandler(CookieContainer cookies, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var header = cookies.GetCookieHeader(uri);
            if (header.Length > 0)
            {
                request.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var value in values)
                {
                    cookies.SetCookies(uri, value);
                }
            }

            return response;
        }
    }
}

internal static class McpClientTesting
{
    public const string ClientsPath = "/api/management/v1/mcp/clients";

    // The instance defaults of the settings area, written as the host's own code.
    public static async Task SetPolicyAsync(FluentCmsTestHost host, McpClientRegistrationMode mode, string? defaultRole = null, int maxPending = 100, int perHour = 100, int maxClients = 1000, int maxConsents = 20)
    {
        await using var scope = host.Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), scope.ServiceProvider.GetRequiredService<TimeProvider>(), Origin.Seed, new SiteId(DefaultSite.Id));
        await scope.ServiceProvider.GetRequiredService<ISettings<McpSettings>>().SetDefaultAsync(
            new McpSettings { ClientRegistration = mode, DefaultClientRole = defaultRole, MaxPendingClients = maxPending, RegistrationsPerHour = perHour, MaxClients = maxClients, MaxConsentsPerUser = maxConsents },
            Token);
    }

    public static async Task<JsonArray> ListAsync(HttpClient admin)
    {
        using var response = await admin.GetAsync(new Uri(ClientsPath, UriKind.Relative), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await JsonAsync(response))["items"]!.AsArray();
    }

    // The client of the list with the OAuth client id, or null.
    public static async Task<JsonNode?> FindAsync(HttpClient admin, string clientId) =>
        (await ListAsync(admin)).FirstOrDefault(item => item!["clientId"]!.GetValue<string>() == clientId);

    public static async Task<Guid> IdOfAsync(HttpClient admin, string clientId) =>
        (await FindAsync(admin, clientId)).ShouldNotBeNull()["id"]!.GetValue<Guid>();

    public static async Task<HttpResponseMessage> ApproveAsync(HttpClient admin, Guid id, string? role)
    {
        using var content = new StringContent(role is null ? "{}" : $$"""{ "role": "{{role}}" }""", Encoding.UTF8, "application/json");
        return await admin.PostAsync(new Uri($"{ClientsPath}/{id:D}/approve", UriKind.Relative), content, Token);
    }

    public static Task<HttpResponseMessage> RejectAsync(HttpClient admin, Guid id) =>
        admin.PostAsync(new Uri($"{ClientsPath}/{id:D}/reject", UriKind.Relative), null, Token);

    // The end of the caller's own consent to a client, which frees its place.
    public static Task<HttpResponseMessage> EndConsentAsync(HttpClient caller, Guid id) =>
        caller.DeleteAsync(new Uri($"/api/management/v1/mcp/consents/{id:D}", UriKind.Relative), Token);

    public static Task<HttpResponseMessage> RevokeAsync(HttpClient admin, Guid id) =>
        admin.DeleteAsync(new Uri($"{ClientsPath}/{id:D}", UriKind.Relative), Token);

    // The bulk removal of the clients the open policy approved, with the body as it is given.
    public static async Task<HttpResponseMessage> RemoveAsync(HttpClient admin, object body)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json");
        return await admin.PostAsync(new Uri($"{ClientsPath}/remove", UriKind.Relative), content, Token);
    }

    // The body of an administrator's registration; a null leaves the member out.
    public static string Registration(string? name, string? role, params string[] redirectUris)
    {
        var body = new JsonObject { ["redirectUris"] = new JsonArray([.. redirectUris.Select(uri => JsonValue.Create(uri))]) };
        if (name is not null)
        {
            body["name"] = name;
        }

        if (role is not null)
        {
            body["role"] = role;
        }

        return body.ToJsonString();
    }

    public static async Task<HttpResponseMessage> RegisterAsync(HttpClient admin, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await admin.PostAsync(new Uri(ClientsPath, UriKind.Relative), content, Token);
    }

    // A client an administrator registered for the agent's redirect URI: its OAuth client id and its id in the store.
    public static async Task<(string ClientId, Guid Id)> RegisteredByAdminAsync(HttpClient admin, string role, string name = "Registered agent")
    {
        using var response = await RegisterAsync(admin, Registration(name, role, McpAgent.RedirectUri));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        var client = await JsonAsync(response);
        return (client["clientId"]!.GetValue<string>(), client["id"]!.GetValue<Guid>());
    }

    public static async Task<HttpResponseMessage> ChangeRoleAsync(HttpClient admin, Guid id, string? role)
    {
        using var content = new StringContent(role is null ? "{}" : $$"""{ "role": "{{role}}" }""", Encoding.UTF8, "application/json");
        return await admin.PutAsync(new Uri($"{ClientsPath}/{id:D}/role", UriKind.Relative), content, Token);
    }

    // The activity rows of a client, as a reader of the instance's activity sees them, once the outbox is drained.
    public static async Task<IReadOnlyList<ActivityItem>> RowsAsync(FluentCmsTestHost host, TestUser reader, Guid clientId)
    {
        await DrainOutboxAsync(host);
        return (await host.As(reader).RunAsync<IActivityQuery, PagedResult<ActivityItem>>(query =>
            query.QueryAsync(new ActivityFilter { TargetType = "mcp_client", TargetId = clientId.ToString("D"), IncludeInstance = true }, null, Token))).Items;
    }

    // The id in the store of a client of the configuration, which the Management API does not list.
    public static async Task<Guid> ApplicationIdAsync(FluentCmsTestHost host, string clientId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = (await applications.FindByClientIdAsync(clientId, Token)).ShouldNotBeNull();
        return Guid.Parse((await applications.GetIdAsync(application, Token))!);
    }

    // Registers a client and approves it with a role: its OAuth client id and its id in the store.
    public static async Task<(string ClientId, Guid Id)> ApprovedAsync(McpAgent agent, HttpClient admin, string role, string name = "Test agent")
    {
        var clientId = await agent.RegisteredAsync(name);
        var id = await IdOfAsync(admin, clientId);
        using var approved = await ApproveAsync(admin, id, role);
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync(Token));
        return (clientId, id);
    }

    // The job as the runner runs it: as the system, in a scope of its own.
    public static async Task RunCleanupAsync(FluentCmsTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();
        using var system = SystemOperation.Begin(accessor, scope.ServiceProvider.GetRequiredService<TimeProvider>(), Origin.Job, new SiteId(DefaultSite.Id));
        var job = ActivatorUtilities.CreateInstance<McpClientCleanupJob>(scope.ServiceProvider);
        await job.ExecuteAsync(new McpClientCleanupPayload(), new JobContext(Guid.CreateVersion7(), McpClientCleanupJob.Type, "default", 1, 1, accessor.Current!, Token));
    }

    // As the prune of the authorization server leaves a client once its tokens and authorizations have expired.
    public static async Task ForgetGrantsAsync(FluentCmsTestHost host, Guid application)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        await context.Set<OpenIddictEntityFrameworkCoreToken<Guid>>().Where(token => token.Application!.Id == application).ExecuteDeleteAsync(Token);
        await context.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().Where(authorization => authorization.Application!.Id == application).ExecuteDeleteAsync(Token);
    }

    public static async Task<McpClientState> StateAsync(FluentCmsTestHost host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = (await applications.FindByIdAsync(id.ToString("D"), Token)).ShouldNotBeNull();
        return McpClientState.Of(await applications.GetPropertiesAsync(application, Token)).ShouldNotBeNull();
    }

    // The state of a client that registered before its first use was recorded: the same, without usedAt and usedBy.
    public static async Task ForgetFirstUseAsync(FluentCmsTestHost host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = (await applications.FindByIdAsync(id.ToString("D"), Token)).ShouldNotBeNull();
        var state = McpClientState.Of(await applications.GetPropertiesAsync(application, Token)).ShouldNotBeNull();
        var descriptor = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(descriptor, application, Token);
        descriptor.Properties[AuthorizationServer.McpClientProperty] = (state with { UsedAt = null, UsedBy = null }).ToProperty();
        await applications.UpdateAsync(application, descriptor, Token);
    }

    public const string ConfiguredClient = "consent-app";

    // The start of the host again, with the clients it lists and this one, which asks for consent, so that it removes
    // none of them.
    public static async Task RegisterConfiguredAsync(FluentCmsTestHost host, string redirectUri, string name)
    {
        var options = new ClientOptions();
        foreach (var (id, client) in host.Services.GetRequiredService<ClientOptions>().Applications.Where(item => item.Key != ConfiguredClient))
        {
            options.Applications[id] = client;
        }

        var registration = new ClientRegistration { DisplayName = name, RequireConsent = true };
        registration.RedirectUris.Add(redirectUri);
        options.Applications[ConfiguredClient] = registration;
        await new ClientRegistrationService(host.Services.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<ClientRegistrationService>.Instance).StartAsync(Token);
    }

    public static async Task GrantAsync(FluentCmsTestHost host, Guid user)
    {
        await using var scope = host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<ClientConsents>().GrantAsync(user, ConfiguredClient, ["openid"], Token)).ShouldBe(ConsentGrant.Granted);
    }

    // The user's consent to the configured client, or null.
    public static async Task<object?> ConsentAsync(FluentCmsTestHost host, Guid user)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applications.FindByClientIdAsync(ConfiguredClient, Token);
        return await scope.ServiceProvider.GetRequiredService<ClientConsents>().FindAsync(user, (await applications.GetIdAsync(application!, Token))!, ["openid"], Token);
    }

    public static async Task<IReadOnlyList<string>> ConfiguredRedirectUrisAsync(FluentCmsTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        return [.. await applications.GetRedirectUrisAsync((await applications.FindByClientIdAsync(ConfiguredClient, Token)).ShouldNotBeNull(), Token)];
    }
}
