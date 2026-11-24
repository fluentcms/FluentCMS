using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Idempotency;

// The row a request claimed, with the key and the request it claimed it for, so that it can put the row back once it is gone.
internal sealed record IdempotencyClaim(Guid Id, Guid Owner, Guid SiteId, PrincipalKind Kind, Guid PrincipalId, string KeyHash, string RequestHash);

// What a renewal did: moved the lease on; found the key held or answered by another request; or found the row removed, past
// its window once its lease ran out, and put it back, so that the request holds the key again.
internal enum IdempotencyRenewal
{
    Renewed,
    Lost,
    Restored,
}

internal sealed record StoredResponse(int Status, IReadOnlyDictionary<string, string> Headers, string Body);

// What a request with a key does next: run, holding the claim, or answer what the first request answered.
internal sealed record IdempotencyStart(IdempotencyClaim? Claim, StoredResponse? Stored);

// The keys of spec 4.10 in cms_idempotency_key, one per principal and site. A key is claimed by inserting its row, so the
// unique index decides between two requests that send it at once, on every database. Each call has a scope of its own,
// so a claim commits at once and never joins the transaction of the work it guards.
internal sealed class IdempotencyStore(IServiceScopeFactory scopes, IDataProvider provider, IdempotencyOptions options, TimeProvider time, ILogger<IdempotencyStore> logger)
{
    public const string Header = "Idempotency-Key";

    public const int MaxKeyLength = 255;

    // A row that expired, ended or changed hands between the insert and the read is tried again, a few times at most.
    private const int Attempts = 3;

    // One value of 1 to 255 visible ASCII characters.
    public static string Key(IReadOnlyList<string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count != 1)
        {
            throw Refused(IdempotencyErrorCodes.InvalidKey, $"Send one {Header}.");
        }

        var key = values[0];
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength || key.Any(character => character is < '!' or > '~'))
        {
            throw Refused(IdempotencyErrorCodes.InvalidKey, $"{Header} must be 1 to {MaxKeyLength} visible ASCII characters.");
        }

        return key;
    }

    public async Task<IdempotencyStart> BeginAsync(OperationContext operation, string key, string requestHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var keyHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(key)));
        var siteId = operation.Site.Value;
        var kind = operation.Principal.Kind;
        var principalId = operation.Principal.Id;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            var rows = context.Set<IdempotencyRow>();
            var now = time.GetUtcNow();
            Task<IdempotencyRow?> FindAsync() => rows.AsNoTracking()
                .SingleOrDefaultAsync(found => found.SiteId == siteId && found.PrincipalKind == kind && found.PrincipalId == principalId && found.KeyHash == keyHash, cancellationToken);

            // A key in use is answered from its row without a write, which on SQLite would wait for the running
            // request's transaction. The insert stays the claim: of two that find no row, one gets the unique violation.
            var existing = await FindAsync();
            if (existing is null)
            {
                var row = new IdempotencyRow
                {
                    Id = Guid.CreateVersion7(),
                    SiteId = siteId,
                    PrincipalKind = kind,
                    PrincipalId = principalId,
                    KeyHash = keyHash,
                    RequestHash = requestHash,
                    Owner = Guid.NewGuid(),
                    CreatedAt = now,
                    LeaseUntil = now + options.InProgressTimeout,
                    ExpiresAt = now + options.Window,
                };
                context.Add(row);
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                    return new IdempotencyStart(new IdempotencyClaim(row.Id, row.Owner, siteId, kind, principalId, keyHash, requestHash), null);
                }
                catch (DbUpdateException exception) when (provider.IsUniqueViolation(exception))
                {
                    context.Entry(row).State = EntityState.Detached;
                }

                existing = await FindAsync();
                if (existing is null)
                {
                    continue;
                }
            }

            if (existing.ExpiresAt <= now)
            {
                // A key past its window is claimed afresh, but not while its request still holds the lease, so that a repeat
                // never runs beside it; once that request answers or stops, the key goes.
                if (existing.ResponseStatus is null && existing.LeaseUntil > now)
                {
                    throw Refused(IdempotencyErrorCodes.InProgress, $"A request with this {Header} is still running, past the window of the key; once it has answered, the key is no longer kept, and the same key runs as a new request.");
                }

                await rows.Where(found => found.Id == existing.Id && found.ExpiresAt <= now && (found.ResponseStatus != null || found.LeaseUntil <= now)).ExecuteDeleteAsync(cancellationToken);
                continue;
            }

            if (existing.RequestHash != requestHash)
            {
                throw Refused(IdempotencyErrorCodes.KeyReused, $"The {Header} was used for another request.");
            }

            if (existing.ResponseStatus is { } status)
            {
                return new IdempotencyStart(null, new StoredResponse(status, Headers(existing.ResponseHeaders), existing.ResponseBody ?? ""));
            }

            if (existing.LeaseUntil > now)
            {
                throw InProgress();
            }

            // The holder stopped before it answered, so this request runs in its place, unless the holder renewed the lease
            // since it was read: the lease read is in the match, so that a renewal in between makes the takeover lose.
            var owner = Guid.NewGuid();
            var lease = now + options.InProgressTimeout;
            var read = existing.LeaseUntil;
            var taken = await rows
                .Where(found => found.Id == existing.Id && found.Owner == existing.Owner && found.ResponseStatus == null && found.LeaseUntil == read)
                .ExecuteUpdateAsync(set => set.SetProperty(found => found.Owner, owner).SetProperty(found => found.LeaseUntil, lease), cancellationToken);
            if (taken == 1)
            {
                IdempotencyLog.TakenOver(logger, existing.Id);
                return new IdempotencyStart(new IdempotencyClaim(existing.Id, owner, siteId, kind, principalId, keyHash, requestHash), null);
            }
        }

        throw InProgress();
    }

    // Moves the lease of a claim on while its request runs. When its row no longer matches, the request puts the row back,
    // as it claimed it: the unique index refuses that while another request has the key, taken over after the renewals
    // failed for longer than the lease or claimed afresh, or once the request answered, so the claim is lost; it lets it
    // when the row was removed, past its window once its lease ran out, and the request then holds the key again. Of the
    // request and a repeat that removed the row, only one gets the key back.
    public async Task<IdempotencyRenewal> RenewAsync(IdempotencyClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var now = time.GetUtcNow();
        var lease = now + options.InProgressTimeout;
        if (await context.Set<IdempotencyRow>().Where(row => row.Id == claim.Id && row.Owner == claim.Owner && row.ResponseStatus == null)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseUntil, lease), cancellationToken) == 1)
        {
            return IdempotencyRenewal.Renewed;
        }

        context.Add(new IdempotencyRow
        {
            Id = claim.Id,
            SiteId = claim.SiteId,
            PrincipalKind = claim.Kind,
            PrincipalId = claim.PrincipalId,
            KeyHash = claim.KeyHash,
            RequestHash = claim.RequestHash,
            Owner = claim.Owner,
            CreatedAt = now,
            LeaseUntil = lease,
            ExpiresAt = now + options.Window,
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return IdempotencyRenewal.Restored;
        }
        catch (DbUpdateException exception) when (provider.IsUniqueViolation(exception))
        {
            return IdempotencyRenewal.Lost;
        }
    }

    // Renews the lease of the claim every third of InProgressTimeout until it is disposed, so that a request that runs
    // longer than the lease is not taken over and run a second time while it works. A renewal that fails is tried again;
    // when one finds the key in another request's hands, after renewals failed for a whole lease, Lost is cancelled, so
    // that the request stops rather than run beside the one that took the key. One that finds the row removed puts it back
    // and goes on holding the key. The lease of a request that stopped runs out InProgressTimeout after its last renewal.
    public IdempotencyLease Hold(IdempotencyClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return new IdempotencyLease(this, claim, options.InProgressTimeout / 3, time, logger);
    }

    // What the answer of a request is captured with (IdempotentRequests): the capture writes to memory and needs no client,
    // so the client going away does not stop it, and a retry gets the answer of a write that committed rather than run it
    // again; it stops InProgressTimeout after that, so that a request whose client is gone holds its key no longer than a lease.
    public IdempotencyCapture CaptureBound(CancellationToken aborted) => new(options.InProgressTimeout, time, aborted);

    public void NotCaptured(IdempotencyClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        IdempotencyLog.NotCaptured(logger, claim.Id);
    }

    // Not canceled: the work is done, and a repeat should find its answer.
    public async Task CompleteAsync(IdempotencyClaim claim, StoredResponse response)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(response);

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var headers = JsonSerializer.Serialize(response.Headers);
            var stored = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<IdempotencyRow>()
                .Where(row => row.Id == claim.Id && row.Owner == claim.Owner && row.ResponseStatus == null)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(row => row.ResponseStatus, response.Status)
                        .SetProperty(row => row.ResponseHeaders, headers)
                        .SetProperty(row => row.ResponseBody, response.Body),
                    CancellationToken.None);
            if (stored == 0)
            {
                IdempotencyLog.NotStored(logger, claim.Id, null);
            }
        }
        catch (Exception exception)
        {
            IdempotencyLog.NotStored(logger, claim.Id, exception);
        }
    }

    // Frees the key of a request that answered nothing to keep, so a repeat runs again. A release that fails leaves the
    // key held until its lease runs out.
    public async Task ReleaseAsync(IdempotencyClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<IdempotencyRow>()
                .Where(row => row.Id == claim.Id && row.Owner == claim.Owner && row.ResponseStatus == null)
                .ExecuteDeleteAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            IdempotencyLog.NotReleased(logger, claim.Id, exception);
        }
    }

    private static Dictionary<string, string> Headers(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

    private static ValidationException InProgress() =>
        Refused(IdempotencyErrorCodes.InProgress, $"A request with this {Header} is still running; repeat it once that one has answered.");

    // The answer of a request whose work was stopped, before it answered, because a repeat took its key over. Work that
    // answered first, its write committed, is answered as it is.
    public static ValidationException TakenOver() =>
        Refused(IdempotencyErrorCodes.InProgress, $"A repeat took this {Header} over after its lease ran out, so this request was stopped before it answered, and its write was rolled back.");

    private static ValidationException Refused(string code, string message) => new([new ValidationError(Header, code, message)]);
}

internal sealed class IdempotencyCapture : IDisposable
{
    private readonly CancellationTokenSource _bound;
    private readonly CancellationTokenRegistration _gone;

    public IdempotencyCapture(TimeSpan bound, TimeProvider time, CancellationToken aborted)
    {
        _bound = new CancellationTokenSource(Timeout.InfiniteTimeSpan, time);
        _gone = aborted.Register(() => _bound.CancelAfter(bound));
    }

    public CancellationToken Token => _bound.Token;

    public bool IsCancellationRequested => _bound.IsCancellationRequested;

    public void Dispose()
    {
        _gone.Dispose();
        _bound.Dispose();
    }
}

internal sealed class IdempotencyLease : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationTokenSource _lost = new();
    private readonly Task _renewing;

    public IdempotencyLease(IdempotencyStore store, IdempotencyClaim claim, TimeSpan every, TimeProvider time, ILogger logger) =>
        _renewing = RenewAsync(store, claim, every, time, logger);

    // Cancelled when the key was found in another request's hands.
    public CancellationToken Lost => _lost.Token;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _renewing;
        _stop.Dispose();
        _lost.Dispose();
    }

    private async Task RenewAsync(IdempotencyStore store, IdempotencyClaim claim, TimeSpan every, TimeProvider time, ILogger logger)
    {
        var stop = _stop.Token;
        try
        {
            while (true)
            {
                await Task.Delay(every, time, stop);
                try
                {
                    switch (await store.RenewAsync(claim, stop))
                    {
                        case IdempotencyRenewal.Lost:
                            IdempotencyLog.ClaimLost(logger, claim.Id);
                            await _lost.CancelAsync();
                            return;
                        case IdempotencyRenewal.Restored:
                            IdempotencyLog.ClaimRestored(logger, claim.Id);
                            break;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    IdempotencyLog.NotRenewed(logger, claim.Id, exception);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }
}

internal static partial class IdempotencyLog
{
    [LoggerMessage(EventId = 9140, Level = LogLevel.Warning, Message = "A request took over the Idempotency-Key {Id}, whose first request did not answer within InProgressTimeout.")]
    public static partial void TakenOver(ILogger logger, Guid id);

    [LoggerMessage(EventId = 9141, Level = LogLevel.Error, Message = "The response of the Idempotency-Key {Id} was not stored, because the write failed or another request took the key over after its lease ran out.")]
    public static partial void NotStored(ILogger logger, Guid id, Exception? exception);

    [LoggerMessage(EventId = 9142, Level = LogLevel.Warning, Message = "The Idempotency-Key {Id} was not released; it stays held until its lease runs out.")]
    public static partial void NotReleased(ILogger logger, Guid id, Exception exception);

    [LoggerMessage(EventId = 9143, Level = LogLevel.Error, Message = "Could not register the recurring retention job of the idempotency keys; expired keys stay until it is registered.")]
    public static partial void ScheduleFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9144, Level = LogLevel.Warning, Message = "The lease of the Idempotency-Key {Id} was not renewed while its request runs; it is tried again, and should the lease run out first, a repeat may take the key over, which stops this request.")]
    public static partial void NotRenewed(ILogger logger, Guid id, Exception exception);

    [LoggerMessage(EventId = 9145, Level = LogLevel.Error, Message = "The Idempotency-Key {Id} was taken over by a repeat after its lease ran out while its request still ran; the request is cancelled, and what it had not committed rolls back.")]
    public static partial void ClaimLost(ILogger logger, Guid id);

    [LoggerMessage(EventId = 9146, Level = LogLevel.Warning, Message = "The Idempotency-Key {Id} passed its window and was removed while its request still ran, after its lease ran out; no other request had claimed it, so the request put its claim back and holds the key again.")]
    public static partial void ClaimRestored(ILogger logger, Guid id);

    [LoggerMessage(EventId = 9147, Level = LogLevel.Error, Message = "The answer of the Idempotency-Key {Id} was not captured within InProgressTimeout after its client went away; nothing is kept, the key stays held until its lease runs out, and a retry after that runs the request again.")]
    public static partial void NotCaptured(ILogger logger, Guid id);
}
