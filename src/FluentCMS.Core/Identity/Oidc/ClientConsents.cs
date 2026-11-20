using System.Collections.Immutable;
using FluentCMS.Agents;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
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

// A user's consent to a third-party client (ClientRegistration.RequireConsent) is a permanent OpenIddict authorization of
// the user for the client and the scopes allowed, which the authorize endpoint then issues the client's codes under. Revoking
// it as a session (UserSessions) takes the consent with it.
internal sealed class ClientConsents(
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations,
    TimeProvider time,
    ILogger<ClientConsents> logger,
    IMcpClientRegistrationPolicy? policy = null)
{
    // The parameter the consent page adds to the authorize request when the user refuses.
    public const string DecisionParameter = "fcms_consent";
    public const string Denied = "denied";

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
    // redirect URI of a scheme an application registered for itself, such as com.example.app:/cb, has no host, and its
    // scheme is what says where the code goes.
    internal static string HostOf(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (uri.IdnHost.Length == 0)
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

    // Records that the user allows the client the scopes it may ask for among these; false for a client that asks no
    // consent, and for a consent that would keep one client more than a user may keep.
    public async Task<bool> GrantAsync(Guid userId, string clientId, IEnumerable<string> scopes, CancellationToken cancellationToken = default)
    {
        var request = await DescribeAsync(clientId, scopes, cancellationToken: cancellationToken);
        if (request is null || await applications.FindByClientIdAsync(clientId, cancellationToken) is not { } application)
        {
            return false;
        }

        var subject = userId.ToString("D");
        var applicationId = await applications.GetIdAsync(application, cancellationToken) ?? "";
        if (await CapReachedAsync(subject, application, applicationId, cancellationToken) is { } max)
        {
            OidcLog.ConsentLimitReached(logger, userId, clientId, max);
            return false;
        }

        var identity = TokenClaims.Identity().SetClaim(Claims.Subject, subject);
        await authorizations.CreateAsync(
            identity,
            subject,
            applicationId,
            AuthorizationTypes.Permanent,
            [.. request.Scopes],
            cancellationToken);
        await MarkUsedAsync(application, cancellationToken);
        return true;
    }

    // A consent keeps a client the open policy approved from the cleanup for as long as it stands, so a user who could
    // consent to any number of them could fill MaxClients for good with clients nobody uses (#3397). A user therefore
    // holds consents to MaxConsentsPerUser such clients at most: the answer is that number when this consent would be
    // one more, and null when it may be given. A client an administrator approved, registered or gave a role is not
    // counted and not held back, since the cleanup keeps it whoever consents, and neither is another consent to a client
    // the user already keeps.
    private async Task<int?> CapReachedAsync(string subject, object application, string applicationId, CancellationToken cancellationToken)
    {
        if (!await IsOpenApprovedAsync(application, cancellationToken))
        {
            return null;
        }

        HashSet<string> consented = new(StringComparer.Ordinal);
        await foreach (var authorization in authorizations.FindBySubjectAsync(subject, cancellationToken))
        {
            if (await authorizations.HasTypeAsync(authorization, AuthorizationTypes.Permanent, cancellationToken)
                && await authorizations.HasStatusAsync(authorization, Statuses.Valid, cancellationToken)
                && await authorizations.GetApplicationIdAsync(authorization, cancellationToken) is { } id)
            {
                consented.Add(id);
            }
        }

        if (consented.Contains(applicationId))
        {
            return null;
        }

        var max = (policy is null ? new McpClientRegistrationPolicy(McpClientRegistrationMode.Off) : await policy.GetAsync(cancellationToken)).MaxConsentsPerUser;
        var kept = 0;
        foreach (var id in consented)
        {
            if (await applications.FindByIdAsync(id, cancellationToken) is { } other && await IsOpenApprovedAsync(other, cancellationToken))
            {
                kept++;
            }
        }

        return kept >= max ? max : null;
    }

    private async Task<bool> IsOpenApprovedAsync(object application, CancellationToken cancellationToken) =>
        McpClientState.Of(await applications.GetPropertiesAsync(application, cancellationToken)) is { IsApproved: true, ApprovedBy: null };

    // The first consent to a client that registered itself is its first use, since it asks for consent before any code: the
    // cleanup removes one never used sooner than one whose tokens are gone. A write that crosses another write of the
    // client, a second consent or a change by an administrator, is not tried again: the cleanup writes the first use
    // of a client that holds a consent and has none (McpClients.RemoveUnusedAsync).
    private async Task MarkUsedAsync(object application, CancellationToken cancellationToken)
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
        descriptor.Properties[AuthorizationServer.McpClientProperty] = (state with { UsedAt = time.GetUtcNow() }).ToProperty();
        try
        {
            await applications.UpdateAsync(application, descriptor, cancellationToken);
        }
        catch (OpenIddictExceptions.ConcurrencyException)
        {
        }
    }
}
