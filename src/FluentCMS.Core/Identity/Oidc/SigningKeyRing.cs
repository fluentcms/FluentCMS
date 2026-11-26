using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using OpenIddict.Validation;

namespace FluentCMS.Core.Identity.Oidc;

// The rows of cms_signing_key as an instance last read them, and the key of each that has not retired and that the key
// ring could read.
internal sealed record SigningKeySet(IReadOnlyList<SigningKey> Rows, IReadOnlyDictionary<Guid, SecurityKey> Keys);

// What this instance holds of cms_signing_key. It reads the rows when the OpenIddict options are first built and then every
// 30 seconds, as it reads sites and settings (4.15), since no signal reaches it when a command or another instance changes
// them. The options of the server, and those of the validation, which copies the server's keys, are built again when the set
// changed or a date of a key has passed. An instance that cannot read keeps the set it has and still moves the keys between
// their states by its clock (ADR 0008).
internal sealed class SigningKeyRing(SigningKeyStore store, SecurityOptions security, TimeProvider time, ILogger<SigningKeyRing> logger) : IDisposable
{
    public static readonly TimeSpan ReadInterval = TimeSpan.FromSeconds(30);

    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _reading = new(1, 1);
    private readonly HashSet<Guid> _unreadable = [];
    private readonly Dictionary<string, Guid?> _active = new(StringComparer.Ordinal);
    private SigningKeySet? _set;
    private DateTimeOffset _appliedAt;
    private long _nextRead;
    private long _nextChange;

    public KeyChangeTokenSource<OpenIddictServerOptions> ServerChanges { get; } = new();

    public KeyChangeTokenSource<OpenIddictValidationOptions> ValidationChanges { get; } = new();

    // The server makes its signing keys only while no certificate signs, and its encryption key always.
    private IReadOnlyCollection<string> Generated => security.SigningCertificate.IsConfigured ? [SigningKey.Encryption] : SigningKey.Uses;

    // The set, read the first time it is asked for, which is when the options are first built.
    public SigningKeySet Current
    {
        get
        {
            if (Volatile.Read(ref _set) is { } set)
            {
                return set;
            }

            lock (_sync)
            {
                if (_set is null)
                {
                    var now = time.GetUtcNow();
                    Hold(store.Read(Generated), now);
                    Note(now, log: false);
                }

                return _set!;
            }
        }
    }

    // Before a request is authenticated. Nothing is read until the options were built once: the first read is theirs.
    public ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _set) is null)
        {
            return ValueTask.CompletedTask;
        }

        var now = time.GetUtcNow().UtcTicks;
        return now < Volatile.Read(ref _nextRead) && now < Volatile.Read(ref _nextChange)
            ? ValueTask.CompletedTask
            : new ValueTask(RefreshCoreAsync(cancellationToken));
    }

    public void Dispose()
    {
        _reading.Dispose();
        ServerChanges.Dispose();
        ValidationChanges.Dispose();
    }

    // The requests that find the copy old share one read of it and wait for it, so each is checked against what was read.
    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        await _reading.WaitAsync(cancellationToken);
        try
        {
            var now = time.GetUtcNow();
            var changed = false;
            if (now.UtcTicks >= Volatile.Read(ref _nextRead))
            {
                try
                {
                    // Without the request's token, so that its going away does not fail the read for those that wait.
                    var rows = await store.ReadAsync(Generated, CancellationToken.None);
                    lock (_sync)
                    {
                        changed = Hold(rows, now);
                    }
                }
                catch (Exception exception)
                {
                    Volatile.Write(ref _nextRead, (now + ReadInterval).UtcTicks);
                    OidcLog.KeysNotRead(logger, exception, (int)ReadInterval.TotalSeconds);
                }
            }

            if (changed || now.UtcTicks >= Volatile.Read(ref _nextChange))
            {
                Apply(now);
            }
        }
        finally
        {
            _reading.Release();
        }
    }

    private void Apply(DateTimeOffset now)
    {
        lock (_sync)
        {
            Note(now, log: true);
        }

        // The server's first: the validation copies the server's keys when its own options are built. Each on its own, so
        // that options that could not be built do not keep the others from being built again.
        Signal(ServerChanges.Signal);
        Signal(ValidationChanges.Signal);
    }

    // The monitor builds the options as it is told, and what that throws comes back here; the next request builds them again.
    private void Signal(Action signal)
    {
        try
        {
            signal();
        }
        catch (Exception exception)
        {
            OidcLog.KeysNotApplied(logger, exception);
        }
    }

    // Keeps the material of the keys already held, so a key is unprotected once, and tries again the rows the key ring
    // could not read. True when the rows, their dates or what could be read changed.
    private bool Hold(IReadOnlyList<SigningKey> rows, DateTimeOffset now)
    {
        var previous = _set;
        Dictionary<Guid, SecurityKey> keys = [];
        foreach (var row in rows.Where(row => !SigningKeyStates.IsRetired(row, now)))
        {
            if (previous is not null && previous.Keys.TryGetValue(row.Id, out var held))
            {
                keys[row.Id] = held;
            }
            else if (store.Unprotect(row) is { } key)
            {
                keys[row.Id] = key;
                _unreadable.Remove(row.Id);
            }
            else if (_unreadable.Add(row.Id))
            {
                OidcLog.KeyUnreadable(logger, row.Id, row.Use, row.ActiveFrom);
            }
        }

        Volatile.Write(ref _nextRead, (now + ReadInterval).UtcTicks);
        if (previous is not null && Same(previous.Rows, rows) && previous.Keys.Count == keys.Count && keys.Keys.All(previous.Keys.ContainsKey))
        {
            return false;
        }

        Volatile.Write(ref _set, new SigningKeySet(rows, keys));
        return true;
    }

    // What the options are about to be built from: says which keys retired and which became active since they were last
    // built, and when the next date of a key passes.
    private void Note(DateTimeOffset now, bool log)
    {
        var rows = _set!.Rows;
        foreach (var use in SigningKey.Uses)
        {
            var ofUse = rows.Where(row => row.Use == use).ToList();
            if (log)
            {
                foreach (var retired in ofUse.Where(row => row.RetiredAt is { } at && at > _appliedAt && at <= now))
                {
                    OidcLog.KeyRetired(logger, retired.Id, use, retired.RetiredAt!.Value);
                }
            }

            var active = SigningKeyStates.ActiveOf(ofUse, now);
            if (log && active is not null && _active.GetValueOrDefault(use) != active.Id)
            {
                OidcLog.KeyActive(logger, active.Id, use, active.ActiveFrom);
            }

            _active[use] = active?.Id;
        }

        _appliedAt = now;
        var next = rows.SelectMany(row => new[] { row.ActiveFrom, row.RetiredAt ?? DateTimeOffset.MaxValue }).Where(date => date > now).DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
        Volatile.Write(ref _nextChange, next.UtcTicks);
    }

    private static bool Same(IReadOnlyList<SigningKey> left, IReadOnlyList<SigningKey> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair => pair.First.Id == pair.Second.Id && pair.First.ActiveFrom == pair.Second.ActiveFrom && pair.First.RetiredAt == pair.Second.RetiredAt);
}

// Tells the options monitor of TOptions to build them again.
internal sealed class KeyChangeTokenSource<TOptions> : IOptionsChangeTokenSource<TOptions>, IDisposable
{
    private CancellationTokenSource _source = new();

    public string? Name => Options.DefaultName;

    public IChangeToken GetChangeToken() => new CancellationChangeToken(Volatile.Read(ref _source).Token);

    // The monitor takes the next token before it builds the options, so the source is replaced before the old one fires.
    public void Signal()
    {
        using var fired = Interlocked.Exchange(ref _source, new CancellationTokenSource());
        fired.Cancel();
    }

    public void Dispose() => _source.Dispose();
}

// Reads the keys again when this instance's copy is 30 seconds old, before the request is authenticated against them.
internal sealed class SigningKeyRefreshMiddleware(RequestDelegate next, SigningKeyRing ring)
{
    public Task InvokeAsync(HttpContext context)
    {
        var refresh = ring.RefreshAsync(context.RequestAborted);
        return refresh.IsCompletedSuccessfully ? next(context) : AwaitAsync(refresh, context);
    }

    private async Task AwaitAsync(ValueTask refresh, HttpContext context)
    {
        await refresh;
        await next(context);
    }
}
