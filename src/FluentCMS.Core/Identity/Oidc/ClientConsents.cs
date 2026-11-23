using System.Collections.Immutable;
using FluentCMS.Agents;
using FluentCMS.Core.Storage;
using FluentCMS.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Identity.Oidc;

// What the consent page shows: the client, the scopes of the request that it may ask for, the hosts of every redirect URI the
// client registered, the request's first when it names one of them, since a consent is of the user, the client and the
// scopes and so covers them all, and whether it is an MCP client, whose name was given at its registration, by the client
// itself or by an administrator, and is not checked against anything.
internal sealed record ConsentRequest(string ClientId, string ClientName, IReadOnlyList<string> Scopes)
{
    public IReadOnlyList<string> RedirectHosts { get; init; } = [];

    public bool McpClient { get; init; }
}

internal enum ConsentGrant
{
    Granted,
    Refused,
    LimitReached,

    // The lock of the consents was not free within its wait.
    Busy,
}

// A client the user has a place for (ClientConsents.PlacesAsync): its id in the store, when the user's newest consent to it
// was given, null once only the client's state names the user, and whether a consent of the user to it is valid.
internal sealed record ConsentPlace(string ApplicationId, DateTimeOffset? ConsentedAt, bool Valid);

// A user's consent to a third-party client (ClientRegistration.RequireConsent) is a permanent OpenIddict authorization of
// the user for the client and the scopes allowed, which the authorize endpoint then issues the client's codes under. Revoking
// it as a session (UserSessions) takes the consent with it.
internal sealed class ClientConsents(
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations,
    SystemDbContext context,
    IDistributedLock locks,
    TimeProvider time,
    ILogger<ClientConsents> logger,
    IMcpClientRegistrationPolicy? policy = null)
{
    // The parameter the consent page adds to the authorize request when the user refuses.
    public const string DecisionParameter = "fcms_consent";
    public const string Denied = "denied";

    // The error the consent page ends on, on the hosted error page, when the consent would keep one client too many.
    public const string LimitReachedError = "consent_limit_reached";

    public const string LockName = "fluentcms:mcp-consents";

    // Long enough for a holder that stopped without releasing the lock to lose its lease, as for SuperAdminGuard.
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(45);

    // Null for a client that does not ask for consent, or does not exist, or registered itself and is not approved, so
    // that no consent is recorded for a client before an administrator has let it in.
    public async Task<ConsentRequest?> DescribeAsync(string clientId, IEnumerable<string> scopes, string? redirectUri = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clientId);
        ArgumentNullException.ThrowIfNull(scopes);

        var application = await applications.FindByClientIdAsync(clientId, cancellationToken);
        if (application is null
            || !await applications.HasConsentTypeAsync(application, ConsentTypes.Explicit, cancellationToken))
        {
            return null;
        }

        var state = McpClientState.Of(await applications.GetPropertiesAsync(application, cancellationToken));
        if (state is { IsApproved: false })
        {
            return null;
        }

        List<string> allowed = [];
        foreach (var scope in scopes.Distinct(StringComparer.Ordinal))
        {
            if (scope == Scopes.OpenId || await applications.HasPermissionAsync(application, Permissions.Prefixes.Scope + scope, cancellationToken))
            {
                allowed.Add(scope);
            }
        }

        // A redirect URI the client does not have is refused by the authorize endpoint, so it is not put first.
        var registered = await applications.GetRedirectUrisAsync(application, cancellationToken);
        var first = !string.IsNullOrEmpty(redirectUri) && registered.Contains(redirectUri, StringComparer.Ordinal) ? redirectUri : null;
        List<string> hosts = [.. (first is null ? registered : registered.Where(uri => uri != first).Prepend(first))
            .Select(uri => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? HostOf(parsed) : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)];

        return new ConsentRequest(clientId, await applications.GetDisplayNameAsync(application, cancellationToken) ?? clientId, allowed)
        {
            RedirectHosts = hosts,
            McpClient = state is not null,
        };
    }

    // The ASCII form of the host, so that a letter of another script that looks like a Latin one shows as punycode. A
    // redirect URI of a scheme an application registered for itself goes to that application whatever follows the scheme,
    // so it is named by its scheme: com.example.app:/cb, which has no host, and myapp://callback/x, whose authority is no
    // web host, alike (#3442).
    internal static string HostOf(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (uri.Scheme is not ("https" or "http") || uri.IdnHost.Length == 0)
        {
            return uri.Scheme + ":";
        }

        var host = uri.HostNameType == UriHostNameType.IPv6 ? $"[{uri.IdnHost}]" : uri.IdnHost;
        return uri.IsDefaultPort ? host : $"{host}:{uri.Port}";
    }

    // The user's valid consent to the client that covers every scope, or null.
    public async Task<object?> FindAsync(Guid userId, string applicationId, ImmutableArray<string> scopes, CancellationToken cancellationToken = default)
    {
        await foreach (var authorization in authorizations.FindAsync(userId.ToString("D"), applicationId, Statuses.Valid, AuthorizationTypes.Permanent, scopes, cancellationToken))
        {
            return authorization;
        }

        return null;
    }

    // Records that the user allows the client the scopes it may ask for among these. Refused for a client that asks no
    // consent, and for a consent that would keep one client more than a user may keep.
    public async Task<ConsentGrant> GrantAsync(Guid userId, string clientId, IEnumerable<string> scopes, CancellationToken cancellationToken = default)
    {
        var request = await DescribeAsync(clientId, scopes, cancellationToken: cancellationToken);
        if (request is null || await applications.FindByClientIdAsync(clientId, cancellationToken) is not { } application)
        {
            return ConsentGrant.Refused;
        }

        var applicationId = await applications.GetIdAsync(application, cancellationToken) ?? "";
        if (!await IsOpenApprovedAsync(application, cancellationToken))
        {
            await CreateAsync(userId, applicationId, application, request, cancellationToken);
            return ConsentGrant.Granted;
        }

        // The count and the insert run under the lock, as every other consent that takes a place and every end of one
        // (McpClients.EndConsentAsync) do, so two consents at once cannot each find the other's place free (#3441).
        await using var held = await LockAsync(cancellationToken);
        if (held is null)
        {
            return ConsentGrant.Busy;
        }

        if (await CapReachedAsync(userId, applicationId, cancellationToken) is { } max)
        {
            OidcLog.ConsentLimitReached(logger, userId, clientId, max);
            return ConsentGrant.LimitReached;
        }

        await CreateAsync(userId, applicationId, application, request, cancellationToken);
        return ConsentGrant.Granted;
    }

    // The lock of the consents that take a place: one for the instance, since they are few, bounded by the auth rate limit,
    // and a lock per user would leave a row of cms_lock for each. It is taken before any transaction opens, as the other
    // locks of identity are, since on SQLite a transaction holds the one writer. Null when it was not free within the wait,
    // which each caller answers as a server that is busy.
    internal Task<IDistributedLockHandle?> LockAsync(CancellationToken cancellationToken) =>
        locks.TryAcquireAsync(LockName, LockWait, cancellationToken);

    private async Task CreateAsync(Guid userId, string applicationId, object application, ConsentRequest request, CancellationToken cancellationToken)
    {
        var subject = userId.ToString("D");
        var identity = TokenClaims.Identity().SetClaim(Claims.Subject, subject);
        await authorizations.CreateAsync(
            identity,
            subject,
            applicationId,
            AuthorizationTypes.Permanent,
            [.. request.Scopes],
            cancellationToken);
        await MarkUsedAsync(application, userId, cancellationToken);
    }

    // A consent keeps a client the open policy approved from the cleanup, so a user who could consent to any number of
    // them could fill MaxClients for good with clients nobody uses (#3397). A user therefore takes a place for each such
    // client they keep (PlacesAsync), MaxConsentsPerUser at most: the answer is that number when this consent would take
    // one more, and null when it may be given. A consent to a client the user has a place for takes none, so a user who
    // changes the password allows the same clients again. A client an administrator approved, registered or gave a role
    // is neither counted nor held back, since the cleanup keeps it whoever consents.
    private async Task<int?> CapReachedAsync(Guid userId, string applicationId, CancellationToken cancellationToken)
    {
        var places = await PlacesAsync(userId, cancellationToken);
        if (places.Any(place => place.ApplicationId == applicationId))
        {
            return null;
        }

        var max = (policy is null ? new McpClientRegistrationPolicy(McpClientRegistrationMode.Off) : await policy.GetAsync(cancellationToken)).MaxConsentsPerUser;
        return places.Count >= max ? max : null;
    }

    // The clients the open policy approved that the user has a place for, which is what counts toward MaxConsentsPerUser.
    // A place is taken by a permanent authorization of the user, whatever its status, and by being the user whose consent
    // first used the client (usedBy), for as long as the client is there. Ending a consent by its session or by a change
    // of password therefore frees no place: the client it kept stays for the cleanup's period, first with the revoked
    // authorization and, for the user who first used it, once that is pruned, with its state alone; otherwise one user
    // could consent to the next clients while the last ones wait for the cleanup. The user frees a place by ending it as
    // such (McpClients.EndConsentAsync). It runs under the lock, so it is two queries read without tracking, of the user's
    // permanent authorizations by client and of those clients, whatever else the user holds, such as sign-ins.
    internal async Task<IReadOnlyList<ConsentPlace>> PlacesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var subject = userId.ToString("D");
        var consented = await context.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().AsNoTracking()
            .Where(authorization => authorization.Subject == subject && authorization.Type == AuthorizationTypes.Permanent && authorization.Application != null)
            .GroupBy(authorization => authorization.Application!.Id)
            .Select(group => new
            {
                Id = group.Key,
                At = group.Max(authorization => authorization.CreationDate),
                Valid = group.Max(authorization => authorization.Status == Statuses.Valid ? 1 : 0),
            })
            .ToDictionaryAsync(row => row.Id, cancellationToken);

        var ids = consented.Keys.ToList();
        var marker = McpClientState.UsedByMarker(userId);
        var clients = await context.Set<OpenIddictEntityFrameworkCoreApplication<Guid>>().AsNoTracking()
            .Where(application => application.Properties != null && (ids.Contains(application.Id) || application.Properties.Contains(marker)))
            .Select(application => new { application.Id, application.Properties })
            .ToListAsync(cancellationToken);

        List<ConsentPlace> places = [];
        foreach (var client in clients)
        {
            if (McpClientState.OfStored(client.Properties) is { IsApproved: true, ApprovedBy: null })
            {
                var consent = consented.GetValueOrDefault(client.Id);
                places.Add(new ConsentPlace(
                    client.Id.ToString("D"),
                    consent?.At is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null,
                    consent?.Valid == 1));
            }
        }

        return places;
    }

    private async Task<bool> IsOpenApprovedAsync(object application, CancellationToken cancellationToken) =>
        McpClientState.Of(await applications.GetPropertiesAsync(application, cancellationToken)) is { IsApproved: true, ApprovedBy: null };

    // The first consent to a client that registered itself is its first use, since it asks for consent before any code: the
    // cleanup removes one never used sooner than one whose tokens are gone. A write that crosses another write of the
    // client, a second consent or a change by an administrator, is not tried again: the cleanup writes the first use
    // of a client that holds a consent and has none (McpClients.RemoveUnusedAsync).
    private async Task MarkUsedAsync(object application, Guid userId, CancellationToken cancellationToken)
    {
        var properties = await applications.GetPropertiesAsync(application, cancellationToken);
        if (McpClientState.Of(properties) is not { UsedAt: null } state
            || !properties.TryGetValue(AuthorizationServer.McpClientProperty, out var stored)
            || stored.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(descriptor, application, cancellationToken);
        descriptor.Properties[AuthorizationServer.McpClientProperty] = (state with { UsedAt = time.GetUtcNow(), UsedBy = userId }).ToProperty();
        try
        {
            await applications.UpdateAsync(application, descriptor, cancellationToken);
        }
        catch (OpenIddictExceptions.ConcurrencyException)
        {
        }
    }
}
