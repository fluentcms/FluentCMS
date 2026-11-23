using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using FluentCMS.Agents;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Identity.Oidc;

// What a client asked for in its registration, once DynamicClientRegistration has checked it.
internal sealed record ClientMetadata(string Name, IReadOnlyList<Uri> RedirectUris, IReadOnlyList<string> GrantTypes, IReadOnlyList<string> Scopes);

// The client a registration made, or nothing when too many clients wait for approval.
internal sealed record RegisteredClient(string ClientId, DateTimeOffset IssuedAt, McpClientStatus Status);

// Which cap refused a registration.
internal enum RegistrationCap
{
    Pending,
    Clients,
}

internal sealed record RegistrationResult(RegisteredClient? Client, RegistrationCap? Cap);

// The MCP clients that registered themselves (RFC 7591), over OpenIddict's application manager: the SDK's IMcpClients for
// the MCP plugin, and the registration itself for the endpoint. Such a client is public, uses the code flow with PKCE and
// refresh tokens, asks the user's consent, and may ask for the scopes of AuthorizationServer.RegisteredClientScopes only.
// A client an administrator registers is made the same way. Its state is one property of its application (McpClientState).
// A change and its event commit together, and nothing here checks a permission: the plugin's service does.
internal sealed class McpClients(
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictTokenManager tokens,
    SystemDbContext context,
    IDataProvider provider,
    ClientConsents consents,
    IdentityChanges changes,
    IEventBus events,
    IOperationContextAccessor accessor,
    TimeProvider time,
    ILogger<McpClients> logger) : IMcpClients
{
    public async Task<IReadOnlyList<McpClient>> ListAsync(CancellationToken cancellationToken = default)
    {
        List<McpClient> clients = [];
        await foreach (var application in applications.ListAsync(count: null, offset: null, cancellationToken))
        {
            if (await DescribeAsync(application, cancellationToken) is { } client)
            {
                clients.Add(client);
            }
        }

        var roleIds = clients.Select(client => client.RoleId).OfType<Guid>().Distinct().ToList();
        var roles = await context.Set<Role>().AsNoTracking()
            .Where(role => roleIds.Contains(role.Id))
            .ToDictionaryAsync(role => role.Id, role => role.Name, cancellationToken);

        // The newest token of each client is its last activity: a sign-in or a refresh.
        var ids = clients.Select(client => client.Id).ToList();
        var newest = await context.Set<OpenIddictEntityFrameworkCoreToken<Guid>>().AsNoTracking()
            .Where(token => token.Application != null && ids.Contains(token.Application.Id))
            .GroupBy(token => token.Application!.Id)
            .Select(group => new { Id = group.Key, At = group.Max(token => token.CreationDate) })
            .ToDictionaryAsync(row => row.Id, row => row.At, cancellationToken);

        return
        [
            .. clients
                .Select(client => client with
                {
                    RoleName = client.RoleId is { } role ? roles.GetValueOrDefault(role) : null,
                    LastActivityAt = newest.GetValueOrDefault(client.Id) is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null,
                })
                .OrderByDescending(client => client.RegisteredAt)
                .ThenBy(client => client.ClientId, StringComparer.Ordinal),
        ];
    }

    public async Task<McpClientChange> ApproveAsync(Guid id, string role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);

        try
        {
            return await changes.RunAsync(
                async () =>
                {
                    if (await FindAsync(id, cancellationToken) is not var (application, client, state))
                    {
                        return new McpClientChange(McpClientChangeOutcome.NotFound, null);
                    }

                    if (state.Status != McpClientStatus.Pending)
                    {
                        return new McpClientChange(McpClientChangeOutcome.NotPending, client);
                    }

                    if (await FindRoleAsync(role, cancellationToken) is not { } found)
                    {
                        return new McpClientChange(McpClientChangeOutcome.UnknownRole, client);
                    }

                    var approved = state with
                    {
                        Status = McpClientStatus.Approved,
                        RoleId = found.Id,
                        ApprovedBy = Actor(),
                        ApprovedAt = time.GetUtcNow(),
                    };
                    await StoreAsync(application, approved, cancellationToken);
                    await changes.RaiseAsync(new McpClientApprovedEvent(client.Id, client.ClientId, client.Name, found.Id, found.Name), cancellationToken);
                    return new McpClientChange(McpClientChangeOutcome.Done, With(client, approved) with { RoleName = found.Name });
                },
                cancellationToken);
        }
        catch (OpenIddictExceptions.ConcurrencyException)
        {
            return await ChangedMeanwhileAsync(id, cancellationToken);
        }
    }

    public async Task<McpClientChange> RejectAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await changes.RunAsync(
                async () =>
                {
                    if (await FindAsync(id, cancellationToken) is not var (application, client, state))
                    {
                        return new McpClientChange(McpClientChangeOutcome.NotFound, null);
                    }

                    if (state.Status != McpClientStatus.Pending)
                    {
                        return new McpClientChange(McpClientChangeOutcome.NotPending, client);
                    }

                    var rejected = state with { Status = McpClientStatus.Rejected };
                    await StoreAsync(application, rejected, cancellationToken);
                    await changes.RaiseAsync(new McpClientRejectedEvent(client.Id, client.ClientId, client.Name), cancellationToken);
                    return new McpClientChange(McpClientChangeOutcome.Done, With(client, rejected));
                },
                cancellationToken);
        }
        catch (OpenIddictExceptions.ConcurrencyException)
        {
            return await ChangedMeanwhileAsync(id, cancellationToken);
        }
    }

    // A client for an agent that does not register itself, held to the name and redirect URIs of a registration and approved
    // at once by the caller. No cap or limit of the registration endpoint applies: the caller is an administrator.
    public async Task<McpClientChange> RegisterAsync(McpClientRegistration registration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(registration.Role);

        var (metadata, refused) = DynamicClientRegistration.ForAdministrator(registration.Name, registration.RedirectUris);
        if (metadata is null)
        {
            return new McpClientChange(McpClientChangeOutcome.InvalidRegistration, null) { Error = refused };
        }

        if (await FindRoleAsync(registration.Role, cancellationToken) is not { } role)
        {
            return new McpClientChange(McpClientChangeOutcome.UnknownRole, null);
        }

        var now = time.GetUtcNow();
        var state = new McpClientState(McpClientStatus.Approved, now)
        {
            RoleId = role.Id,
            ApprovedBy = Actor(),
            ApprovedAt = now,
        };
        var clientId = NewClientId();
        var descriptor = Describe(clientId, metadata);
        descriptor.Properties[AuthorizationServer.McpClientProperty] = state.ToProperty();

        return await changes.RunAsync(
            async () =>
            {
                var application = await applications.CreateAsync(descriptor, cancellationToken);
                var client = (await DescribeAsync(application, cancellationToken))! with { RoleName = role.Name };
                await changes.RaiseAsync(new McpClientRegisteredEvent(client.Id, clientId, metadata.Name, StatusName(state.Status), role.Id, Ip: null), cancellationToken);
                return new McpClientChange(McpClientChangeOutcome.Done, client);
            },
            cancellationToken);
    }

    // The tokens of a client carry the role they were issued with, so a new role ends them with the change, and ends the
    // authorizations they were issued under, the users' consents among them. A refresh that read the old role just before
    // the change still issues its tokens, under an authorization that is revoked, so they are refused at their first use.
    // The client's next token comes from a new authorization request, which asks the user's consent again.
    public async Task<McpClientChange> ChangeRoleAsync(Guid id, string role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);

        try
        {
            return await changes.RunAsync(
                async () =>
                {
                    if (await FindAsync(id, cancellationToken) is not var (application, client, state))
                    {
                        return new McpClientChange(McpClientChangeOutcome.NotFound, null);
                    }

                    if (!state.IsApproved)
                    {
                        return new McpClientChange(McpClientChangeOutcome.NotApproved, client);
                    }

                    if (await FindRoleAsync(role, cancellationToken) is not { } found)
                    {
                        return new McpClientChange(McpClientChangeOutcome.UnknownRole, client);
                    }

                    // An administrator acted on the client, so the cleanup and the bulk removal of clients nobody looked
                    // at leave it alone, whether the role is another or the one it has.
                    var changed = state.ApprovedBy is null
                        ? state with { RoleId = found.Id, ApprovedBy = Actor(), ApprovedAt = time.GetUtcNow() }
                        : state with { RoleId = found.Id };
                    if (found.Id == state.RoleId)
                    {
                        if (changed != state)
                        {
                            await StoreAsync(application, changed, cancellationToken);
                        }

                        return new McpClientChange(McpClientChangeOutcome.Done, With(client, changed) with { RoleName = found.Name });
                    }

                    await StoreAsync(application, changed, cancellationToken);
                    await authorizations.RevokeByApplicationIdAsync(id.ToString("D"), cancellationToken);
                    await tokens.RevokeByApplicationIdAsync(id.ToString("D"), cancellationToken);
                    await changes.RaiseAsync(new McpClientRoleChangedEvent(client.Id, client.ClientId, client.Name, found.Id, found.Name, state.RoleId!.Value), cancellationToken);
                    return new McpClientChange(McpClientChangeOutcome.Done, With(client, changed) with { RoleName = found.Name });
                },
                cancellationToken);
        }
        catch (OpenIddictExceptions.ConcurrencyException)
        {
            // The application this change read is not the one in the store any more, so it is not known what it would
            // have changed; the caller reads the client again.
            return new McpClientChange(McpClientChangeOutcome.ChangedMeanwhile, null);
        }
    }

    // The EF store deletes the client's authorizations and tokens with it, and a bearer request reads its token's row, so
    // an access token of the client is refused from the next request on.
    public Task<McpClientChange> RevokeAsync(Guid id, CancellationToken cancellationToken = default) =>
        changes.RunAsync(
            async () =>
            {
                if (await FindAsync(id, cancellationToken) is not var (application, client, _))
                {
                    return new McpClientChange(McpClientChangeOutcome.NotFound, null);
                }

                await applications.DeleteAsync(application, cancellationToken);
                await changes.RaiseAsync(new McpClientRevokedEvent(client.Id, client.ClientId, client.Name), cancellationToken);
                return new McpClientChange(McpClientChangeOutcome.Done, client);
            },
            cancellationToken);

    // A client the open policy approved has no administrator who would remove it. One no user ever consented to goes after
    // the short period, so that clients registered only to fill MaxClients do not hold it; one whose tokens and
    // authorizations are all gone goes after the long one. The candidates are found in SQL, and each is read again just
    // before it is deleted, so that a client that got its first token in between stays. They go in the order of their ids,
    // and a delete that fails for another reason than a race is logged and passed over, so that one client that cannot be
    // deleted does not keep the run from the others; the run then fails with it, and the job runner tries again (#3448).
    public async Task<int> RemoveUnusedAsync(DateTimeOffset neverUsedBefore, DateTimeOffset unusedBefore, CancellationToken cancellationToken = default)
    {
        await MarkUsedAsync(cancellationToken);

        var unused = await McpApplications()
            .Where(application => !context.Set<OpenIddictEntityFrameworkCoreToken<Guid>>().Any(token => token.Application != null && token.Application.Id == application.Id)
                && !context.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().Any(authorization => authorization.Application != null && authorization.Application.Id == application.Id))
            .OrderBy(application => application.Id)
            .Select(application => application.Id)
            .ToListAsync(cancellationToken);

        var removed = 0;
        List<Exception> failures = [];
        foreach (var id in unused)
        {
            if (await applications.FindByIdAsync(id.ToString("D"), cancellationToken) is not { } application
                || McpClientState.Of(await applications.GetPropertiesAsync(application, cancellationToken)) is not { IsApproved: true, ApprovedBy: null } state
                || !(state.UsedAt is null ? state.RegisteredAt < neverUsedBefore : state.RegisteredAt < unusedBefore)
                || await HasGrantsAsync(id, cancellationToken))
            {
                continue;
            }

            try
            {
                await applications.DeleteAsync(application, cancellationToken);
                removed++;
            }
            catch (OpenIddictExceptions.ConcurrencyException)
            {
                // Changed since it was read, by an approval or a first use; the next run looks at it again.
            }
            catch (DbUpdateException exception) when (CrossedAGrant(exception))
            {
                // A consent or a token arrived between the last look and the delete, which its foreign key refuses, or
                // which the database answers as a deadlock under the store's serializable transaction. The failed delete
                // is still tracked, and would fail every later one of this run with it.
                context.ChangeTracker.Clear();
                OidcLog.ClientDeleteRefused(logger, exception, id);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                context.ChangeTracker.Clear();
                OidcLog.ClientDeleteFailed(logger, exception, id);
                failures.Add(exception);
            }
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException($"Could not delete {failures.Count} MCP clients; each is logged with its id (event {OidcLog.ClientDeleteFailedId}).", failures);
        }

        return removed;
    }

    // What a delete gets when a consent or a token of the client arrived during it: the refusal of a foreign key, or, under
    // a serializable transaction such as the store's own, the deadlock that SQL Server answers for the same race. A
    // serialization failure of PostgreSQL (40001) has no member of the providers to tell it, so it is thrown.
    private bool CrossedAGrant(Exception exception) => provider.IsForeignKeyViolation(exception) || provider.IsDeadlock(exception);

    // A client that holds a token or an authorization was used, whatever its state says: one that registered before the
    // first use was recorded has no usedAt, and neither has one whose first consent crossed another write of the client
    // (ClientConsents.MarkUsedAsync). The cleanup writes it, with the user of the oldest authorization, while the grant
    // is there to tell, so that such a client is not taken for one never used once its grants are gone, and keeps its
    // place among the clients that user may keep. usedBy gives a place, so it is written under the lock of the consents:
    // otherwise a user whose consent ended between the read of the oldest authorization and the write would be named
    // again, and hold a place without a consent (#3441).
    private async Task MarkUsedAsync(CancellationToken cancellationToken)
    {
        var unmarked = await McpApplications()
            .Where(application => !application.Properties!.Contains(UsedByMarker)
                && !application.Properties.Contains(ApprovedByMarker)
                && (context.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().Any(authorization => authorization.Application != null && authorization.Application.Id == application.Id)
                    || context.Set<OpenIddictEntityFrameworkCoreToken<Guid>>().Any(token => token.Application != null && token.Application.Id == application.Id)))
            .Select(application => application.Id)
            .ToListAsync(cancellationToken);
        if (unmarked.Count == 0)
        {
            return;
        }

        await using var held = await consents.LockAsync(cancellationToken)
            ?? throw new TimeoutException($"The lock {ClientConsents.LockName} was not free, so the first uses of the MCP clients were not written; the next run writes them.");
        foreach (var id in unmarked)
        {
            if (await applications.FindByIdAsync(id.ToString("D"), cancellationToken) is not { } application
                || McpClientState.Of(await applications.GetPropertiesAsync(application, cancellationToken)) is not { IsApproved: true, ApprovedBy: null, UsedBy: null } state)
            {
                continue;
            }

            var first = await context.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().AsNoTracking()
                .Where(authorization => authorization.Application != null && authorization.Application.Id == id)
                .OrderBy(authorization => authorization.CreationDate)
                .Select(authorization => new { authorization.CreationDate, authorization.Subject })
                .FirstOrDefaultAsync(cancellationToken);
            var marked = state with
            {
                UsedAt = state.UsedAt ?? (first?.CreationDate is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : time.GetUtcNow()),
                UsedBy = Guid.TryParse(first?.Subject, out var user) ? user : null,
            };
            if (marked == state)
            {
                continue;
            }

            try
            {
                await StoreAsync(application, marked, cancellationToken);
            }
            catch (OpenIddictExceptions.ConcurrencyException)
            {
                // Changed since it was read; the next run looks at it again.
            }
        }
    }

    // The clients the open policy approved that registered in a span of time, removed at an administrator's word with
    // their sessions and tokens, each as RevokeAsync removes one and with its event. A client an administrator approved,
    // registered or gave a role carries that administrator and is never one of them, and each is read again just before
    // it is deleted, so that one an administrator acted on meanwhile stays. Each delete is a transaction of its own with
    // its event, so a call removes limit clients at most and the caller repeats it for the rest. The candidates are found
    // before any transaction: the status and approvedBy in SQL, and the span in the state that the same query reads, since
    // registeredAt is in the stored JSON, which the system model has no function of the providers for (#3448).
    public async Task<int> RemoveOpenApprovedAsync(DateTimeOffset? registeredFrom, DateTimeOffset? registeredBefore, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var rows = await McpApplications()
            .Where(application => application.Properties!.Contains(ApprovedMarker) && !application.Properties.Contains(ApprovedByMarker))
            .Select(application => new { application.Id, application.Properties })
            .ToListAsync(cancellationToken);
        var candidates = rows
            .Select(row => (row.Id, State: McpClientState.OfStored(row.Properties)))
            .Where(row => row.State is { IsApproved: true, ApprovedBy: null } state && !(state.RegisteredAt < registeredFrom) && !(state.RegisteredAt >= registeredBefore))
            .OrderBy(row => row.State!.RegisteredAt)
            .Select(row => row.Id)
            .ToList();

        var removed = 0;
        foreach (var id in candidates)
        {
            if (removed >= limit)
            {
                break;
            }

            try
            {
                var gone = await changes.RunAsync(
                    async () =>
                    {
                        if (await FindAsync(id, cancellationToken) is not var (application, client, state)
                            || state is not { IsApproved: true, ApprovedBy: null }
                            || state.RegisteredAt < registeredFrom
                            || state.RegisteredAt >= registeredBefore)
                        {
                            return false;
                        }

                        await applications.DeleteAsync(application, cancellationToken);
                        await changes.RaiseAsync(new McpClientRevokedEvent(client.Id, client.ClientId, client.Name), cancellationToken);
                        return true;
                    },
                    cancellationToken);
                removed += gone ? 1 : 0;
            }
            catch (OpenIddictExceptions.ConcurrencyException)
            {
                // Changed since it was read, as by an approval or a role change; it is left.
            }
            catch (DbUpdateException exception) when (CrossedAGrant(exception))
            {
                // A consent or a token arrived during the delete, which its foreign key refuses; the failed delete is
                // still tracked, and would fail every later one of this call with it. Any other failure of the save, the
                // write of the event included, ends the call: the clients removed before it stay removed, each with its
                // activity row (#3448).
                context.ChangeTracker.Clear();
                OidcLog.ClientDeleteRefused(logger, exception, id);
            }
        }

        return removed;
    }

    public async Task<IReadOnlyList<McpClientConsent>> ListConsentsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        List<McpClientConsent> listed = [];
        foreach (var place in await consents.PlacesAsync(userId, cancellationToken))
        {
            if (await applications.FindByIdAsync(place.ApplicationId, cancellationToken) is { } application
                && await DescribeAsync(application, cancellationToken) is { } client)
            {
                listed.Add(new McpClientConsent(client, place.ConsentedAt, place.Valid));
            }
        }

        return
        [
            .. listed
                .OrderByDescending(consent => consent.ConsentedAt ?? consent.Client.UsedAt)
                .ThenBy(consent => consent.Client.ClientId, StringComparer.Ordinal),
        ];
    }

    // A place is held by the user's authorizations of the client, valid or revoked, and by usedBy, so ending a consent as a
    // session does leaves it taken (ClientConsents.PlacesAsync). This frees it: the authorizations go with their tokens,
    // which a revoke would leave until the prune, and usedBy no longer names the user. A client that then holds no grant
    // of anyone loses usedAt too and is taken for one never used, which the cleanup removes after NeverUsedClientHours;
    // otherwise a user could consent to a client, free its place and consent to the next, each kept for the cleanup's
    // long period. It runs under the lock of the consents that take a place, so a consent of the user at the same moment
    // counts what this left (#3441). A client that cannot count for the user is answered before the lock, so that a call
    // with any id keeps no consent waiting, and everything is read again under it.
    public async Task<McpClientChange> EndConsentAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var subject = userId.ToString("D");
        var marker = McpClientState.UsedByMarker(userId);
        if (!await McpApplications().AnyAsync(
                application => application.Id == id
                    && application.Properties!.Contains(ApprovedMarker)
                    && !application.Properties.Contains(ApprovedByMarker)
                    && (application.Properties.Contains(marker)
                        || context.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().Any(authorization => authorization.Application != null && authorization.Application.Id == id && authorization.Subject == subject)),
                cancellationToken))
        {
            return new McpClientChange(McpClientChangeOutcome.NotFound, null);
        }

        await using var held = await consents.LockAsync(cancellationToken)
            ?? throw new ServiceUnavailableException($"Other consents to MCP clients are being changed, so the consent to {id:D} was not ended; try again in a moment.");
        try
        {
            return await changes.RunAsync(
                async () =>
                {
                    if (await FindAsync(id, cancellationToken) is not var (application, client, state)
                        || state is not { IsApproved: true, ApprovedBy: null })
                    {
                        return new McpClientChange(McpClientChangeOutcome.NotFound, null);
                    }

                    var granted = await context.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().AsNoTracking()
                        .Where(authorization => authorization.Application != null && authorization.Application.Id == id && authorization.Subject == subject)
                        .Select(authorization => new { authorization.Id, authorization.Status })
                        .ToListAsync(cancellationToken);
                    if (granted.Count == 0 && state.UsedBy != userId)
                    {
                        return new McpClientChange(McpClientChangeOutcome.NotFound, null);
                    }

                    foreach (var grant in granted)
                    {
                        if (await authorizations.FindByIdAsync(grant.Id.ToString("D"), cancellationToken) is { } authorization)
                        {
                            await authorizations.DeleteAsync(authorization, cancellationToken);
                        }
                    }

                    var freed = state.UsedBy == userId ? state with { UsedBy = null } : state;
                    if (freed.UsedBy is null && !await HasGrantsAsync(id, cancellationToken))
                    {
                        freed = freed with { UsedAt = null };
                    }

                    if (freed != state)
                    {
                        await StoreAsync(application, freed, cancellationToken);
                    }

                    var name = await context.Set<CmsUser>().AsNoTracking().Where(user => user.Id == userId).Select(user => user.UserName).FirstOrDefaultAsync(cancellationToken);
                    foreach (var ended in granted.Where(grant => grant.Status == Statuses.Valid))
                    {
                        await changes.RaiseAsync(new UserSessionsEndedEvent(userId, name ?? "", SessionEndReasons.Revoked, ended.Id), cancellationToken);
                    }

                    return new McpClientChange(McpClientChangeOutcome.Done, With(client, freed));
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is OpenIddictExceptions.ConcurrencyException || (exception is DbUpdateException && CrossedAGrant(exception)))
        {
            // The client or a consent changed since it was read, or a token of the consent was issued while it was
            // deleted, which the foreign key refuses; the transaction rolled back whole, so nothing was ended, and the
            // caller tries again.
            return new McpClientChange(McpClientChangeOutcome.ChangedMeanwhile, null);
        }
    }

    private async Task<bool> HasGrantsAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Set<OpenIddictEntityFrameworkCoreToken<Guid>>().AnyAsync(token => token.Application != null && token.Application.Id == id, cancellationToken)
        || await context.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().AnyAsync(authorization => authorization.Application != null && authorization.Application.Id == id, cancellationToken);

    public async Task<int> RemoveUnapprovedAsync(DateTimeOffset registeredBefore, CancellationToken cancellationToken = default)
    {
        List<object> stale = [];
        await foreach (var application in applications.ListAsync(count: null, offset: null, cancellationToken))
        {
            if (McpClientState.Of(await applications.GetPropertiesAsync(application, cancellationToken)) is { IsApproved: false } state
                && state.RegisteredAt < registeredBefore)
            {
                stale.Add(application);
            }
        }

        foreach (var application in stale)
        {
            await applications.DeleteAsync(application, cancellationToken);
        }

        return stale.Count;
    }

    // Registers a client: approved at once with the default role under the open policy when that role exists, and pending
    // otherwise. Refused, in every mode, when there are as many MCP clients as the policy allows, and, for a client that
    // would wait, when as many wait for approval. The counts come before the insert, so registrations that cross may pass a
    // cap by the few in flight.
    public async Task<RegistrationResult> RegisterAsync(ClientMetadata metadata, McpClientRegistrationPolicy policy, EventSource source, string? ip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(source);

        if (await McpApplications().CountAsync(cancellationToken) >= policy.MaxClients)
        {
            return new RegistrationResult(null, RegistrationCap.Clients);
        }

        var role = policy.Mode == McpClientRegistrationMode.Open && policy.DefaultRole is { Length: > 0 } named
            ? await FindRoleAsync(named, cancellationToken)
            : null;
        if (role is null && await McpApplications().CountAsync(application => application.Properties!.Contains(PendingMarker), cancellationToken) >= policy.MaxPendingClients)
        {
            return new RegistrationResult(null, RegistrationCap.Pending);
        }

        var now = time.GetUtcNow();
        var state = role is null
            ? new McpClientState(McpClientStatus.Pending, now)
            : new McpClientState(McpClientStatus.Approved, now) { RoleId = role.Id, ApprovedAt = now };

        var clientId = NewClientId();
        var descriptor = Describe(clientId, metadata);
        descriptor.Properties[AuthorizationServer.McpClientProperty] = state.ToProperty();

        return await changes.RunAsync(
            async () =>
            {
                var application = await applications.CreateAsync(descriptor, cancellationToken);
                var id = Guid.Parse(await applications.GetIdAsync(application, cancellationToken) ?? "");
                var registered = new McpClientRegisteredEvent(id, clientId, metadata.Name, StatusName(state.Status), state.RoleId, ip)
                {
                    Source = source with { ClientId = clientId },
                };
                await events.PublishAsync(registered, cancellationToken);
                return new RegistrationResult(new RegisteredClient(clientId, now, state.Status), null);
            },
            cancellationToken);
    }

    public static string StatusName(McpClientStatus status) => status switch
    {
        McpClientStatus.Approved => "approved",
        McpClientStatus.Rejected => "rejected",
        _ => "pending",
    };

    internal static OpenIddictApplicationDescriptor Describe(string clientId, ClientMetadata metadata)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = metadata.Name,
        };

        descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.Endpoints.Revocation);
        descriptor.Permissions.Add(Permissions.Endpoints.EndSession);
        descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        foreach (var grant in metadata.GrantTypes)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.GrantType + grant);
        }

        // openid needs no permission; any other scope does, so a scope left out here is refused at the authorize endpoint.
        foreach (var scope in metadata.Scopes.Where(scope => scope != Scopes.OpenId && AuthorizationServer.RegisteredClientScopes.Contains(scope, StringComparer.Ordinal)))
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }

        foreach (var uri in metadata.RedirectUris)
        {
            descriptor.RedirectUris.Add(uri);
        }

        return descriptor;
    }

    private static readonly string StateMarker = "\"" + AuthorizationServer.McpClientProperty + "\":{";

    private static readonly string PendingMarker = "\"status\":\"pending\"";

    private static readonly string ApprovedMarker = "\"status\":\"approved\"";

    // A state is written without its null members, so one without these was approved by no administrator, or names no
    // user who first used it.
    private static readonly string ApprovedByMarker = "\"approvedBy\":";

    private static readonly string UsedByMarker = "\"usedBy\":";

    private static string NewClientId() => "mcp-" + RandomNumberGenerator.GetHexString(32, lowercase: true);

    // Who approves, registers or gives a role: the principal of the operation, or the system where code calls with none,
    // so that such a client is never taken for one the open policy approved.
    private Guid Actor() => (accessor.Current?.AuditPrincipal ?? Principal.System).Id;

    // The applications that carry the state of an MCP client, found in the stored JSON of their properties, so that a count
    // is one query. OpenIddict writes the properties without whitespace, and McpClientState is the only object with a status.
    private IQueryable<OpenIddictEntityFrameworkCoreApplication<Guid>> McpApplications() =>
        context.Set<OpenIddictEntityFrameworkCoreApplication<Guid>>().Where(application => application.Properties != null && application.Properties.Contains(StateMarker));

    // Another change got there first, so the client is no longer what this one read.
    private async Task<McpClientChange> ChangedMeanwhileAsync(Guid id, CancellationToken cancellationToken) =>
        await FindAsync(id, cancellationToken) is var (_, client, _)
            ? new McpClientChange(McpClientChangeOutcome.NotPending, client)
            : new McpClientChange(McpClientChangeOutcome.NotFound, null);

    private async Task<(object Application, McpClient Client, McpClientState State)?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await applications.FindByIdAsync(id.ToString("D"), cancellationToken) is not { } application
            || McpClientState.Of(await applications.GetPropertiesAsync(application, cancellationToken)) is not { } state
            || await DescribeAsync(application, cancellationToken) is not { } client)
        {
            return null;
        }

        return (application, client, state);
    }

    // Null for a client that did not register itself, which this service neither lists nor changes.
    private async Task<McpClient?> DescribeAsync(object application, CancellationToken cancellationToken)
    {
        if (McpClientState.Of(await applications.GetPropertiesAsync(application, cancellationToken)) is not { } state)
        {
            return null;
        }

        var clientId = await applications.GetClientIdAsync(application, cancellationToken) ?? "";
        var permissions = await applications.GetPermissionsAsync(application, cancellationToken);
        var client = new McpClient(
            Guid.Parse(await applications.GetIdAsync(application, cancellationToken) ?? ""),
            clientId,
            await applications.GetDisplayNameAsync(application, cancellationToken) ?? clientId,
            state.Status,
            [.. await applications.GetRedirectUrisAsync(application, cancellationToken)],
            state.RegisteredAt)
        {
            Scopes =
            [
                Scopes.OpenId,
                .. permissions
                    .Where(permission => permission.StartsWith(Permissions.Prefixes.Scope, StringComparison.Ordinal))
                    .Select(permission => permission[Permissions.Prefixes.Scope.Length..])
                    .Order(StringComparer.Ordinal),
            ],
        };
        return With(client, state);
    }

    private static McpClient With(McpClient client, McpClientState state) => client with
    {
        Status = state.Status,
        RoleId = state.RoleId,
        ApprovedBy = state.ApprovedBy,
        ApprovedAt = state.ApprovedAt,
        UsedAt = state.UsedAt,
        UsedBy = state.UsedBy,
    };

    private async Task StoreAsync(object application, McpClientState state, CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(descriptor, application, cancellationToken);
        descriptor.Properties[AuthorizationServer.McpClientProperty] = state.ToProperty();
        await applications.UpdateAsync(application, descriptor, cancellationToken);
    }

    // A role by its id, or an instance-wide role by its exact name, since a name is unique only within its site.
    private async Task<Role?> FindRoleAsync(string reference, CancellationToken cancellationToken)
    {
        var trimmed = reference.Trim();
        return Guid.TryParse(trimmed, out var id)
            ? await context.Set<Role>().AsNoTracking().FirstOrDefaultAsync(role => role.Id == id, cancellationToken)
            : await context.Set<Role>().AsNoTracking().FirstOrDefaultAsync(role => role.SiteId == null && role.Name == trimmed, cancellationToken);
    }
}
