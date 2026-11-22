using System.Collections.Concurrent;
using System.Reflection;
using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Telemetry;
using FluentCMS.Hooks;
using FluentCMS.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentCMS.Core.Content.Hooks;

// Runs the hooks of one kind for one entry. Hooks for ContentDocument (every type) and for the content class of the type,
// and for its base classes, form one sequence ordered by [HookOrder], then by IHookTiebreaker rank. Hooks that still tie keep
// the order of resolution: all-types hooks first, then base classes before derived ones, each in registration order.
// The first hook that cancels throws HookRejectedException and the hooks after it do not run.
// The changed paths are those between previous and entry, computed again before each before hook so that a hook sees the
// changes of the hooks before it; an operation that changes no field passes the stored state as previous.
// Only before hooks change the entry: an after hook works on a copy. A hook of a plugin disabled in the operation's site does not run.
// Each hook runs for FluentCms:Content:HookTimeout at most, since it holds the write's transaction, and on SQLite the
// database's one writer: one that runs longer has its token cancelled and is no longer waited for, and the write fails with
// FCMS2410 naming it and rolls back. A hook that ignores its token is left to end on its own, as an event handler is, still
// holding the scope's services, and so is one whose caller is cancelled while it runs: the scope is then marked and writes
// and commits nothing more (HookScopeGuard). A hook starts on the thread pool, so that one that blocks before its first
// await is bounded too.
public sealed class HookPipeline(IServiceProvider services)
{
    private static readonly ConcurrentDictionary<Type, int> Orders = new();

    private readonly HookScopeGuard _guard = services.GetService(typeof(HookScopeGuard)) as HookScopeGuard ?? new HookScopeGuard();
    private readonly TimeProvider _time = services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
    private readonly ContentWriteOptions _options = services.GetService(typeof(ContentWriteOptions)) as ContentWriteOptions ?? new ContentWriteOptions();
    private readonly ILogger _logger = (services.GetService(typeof(ILogger<HookPipeline>)) as ILogger) ?? NullLogger.Instance;

    // The hooks of a scope do not change, so each kind is resolved and ordered once per content class and scope, not on every
    // call. A hook registered as transient therefore lives as long as the scope, as a scoped one does.
    private readonly Dictionary<(HookKind Kind, Type? ClrType), List<BoundHook>> _resolved = [];

    // A hook of this scope ran past its time or was left by its caller, and may still be running on the scope's services.
    internal bool LeftRunning => _guard.LeftRunning;

    internal void ThrowIfLeftRunning() => _guard.ThrowIfLeftRunning();

    public async ValueTask RunAsync(
        HookKind kind,
        SchemaType type,
        OperationContext operation,
        ContentDocument entry,
        ContentDocument? previous,
        CancellationToken cancellationToken) =>
        await RunAsync(kind, type, operation, entry, previous, beforeFirst: null, cancellationToken);

    // For an operation on every locale of the entry, while entry holds the values of one of them.
    internal async ValueTask RunAsync(
        HookKind kind,
        SchemaType type,
        OperationContext operation,
        ContentDocument entry,
        ContentDocument? previous,
        IReadOnlyList<string> locales,
        CancellationToken cancellationToken) =>
        await RunAsync(kind, type, operation, entry, previous, beforeFirst: null, cancellationToken, locales);

    // Whether a hook ran. beforeFirst runs just before the first hook does, while the entry is still as it was given, so that
    // a caller compares with it only when there is something to compare. Without locales the operation is on the entry's own.
    internal async ValueTask<bool> RunAsync(
        HookKind kind,
        SchemaType type,
        OperationContext operation,
        ContentDocument entry,
        ContentDocument? previous,
        Action? beforeFirst,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? locales = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(entry);

        if (!_resolved.TryGetValue((kind, type.ClrType), out var hooks))
        {
            hooks = Resolve(kind, type);
            _resolved[(kind, type.ClrType)] = hooks;
        }

        if (hooks.Count > 0 && services.GetService(typeof(PluginState)) is PluginState plugins)
        {
            hooks = await plugins.EnabledAsync(hooks, hook => hook.ImplementationType, operation.Site.Value, cancellationToken);
        }

        if (hooks.Count == 0)
        {
            return false;
        }

        beforeFirst?.Invoke();

        using var span = FluentCmsTelemetry.Start("hook", kind.ToString(), type.Name, operation);
        span?.SetTag(FluentCmsTelemetry.Tags.Hooks, hooks.Count);

        var mutable = kind is HookKind.BeforeValidate or HookKind.BeforeCreate or HookKind.BeforeUpdate
            or HookKind.BeforeDelete or HookKind.BeforePublish or HookKind.BeforeUnpublish or HookKind.BeforeRestore;
        var session = new Session(
            type.ClrType,
            operation,
            mutable ? entry : ContentCopy.Of(entry),
            previous,
            locales ?? (entry.GetValueOrDefault("locale") is string locale ? [locale] : []),
            services.GetService(typeof(IHookEntryMapper)) as IHookEntryMapper);

        var first = true;
        foreach (var hook in hooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _guard.ThrowIfLeftRunning();
            if (mutable && !first)
            {
                session.RefreshChangedPaths();
            }

            first = false;

            await InvokeWithinTimeoutAsync(hook, kind, type, session, mutable, cancellationToken);
        }

        // A write nested in the last hook may have left one running, whatever that hook made of the failure.
        _guard.ThrowIfLeftRunning();
        return true;
    }

    private async Task InvokeWithinTimeoutAsync(BoundHook hook, HookKind kind, SchemaType type, Session session, bool mutable, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_options.HookTimeout, _time);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = stop.Token;
        var name = hook.ImplementationType.FullName ?? hook.ImplementationType.Name;
        var started = _time.GetTimestamp();

        // The token is also the work item's: a hook whose caller left before the pool got to it does not start.
        var running = Task.Run(() => hook.InvokeAsync(session, mutable, token).AsTask(), token);
        try
        {
            await running.WaitAsync(token);

            // A hook that swallows the cancellation still ran past its time.
            timeout.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _guard.MarkLeftRunning(name);
            ContentLog.HookTimedOut(_logger, kind, name, type.Name, _options.HookTimeout);
            throw new HookRejectedException(ContentErrorCodes.HookTimedOut, $"The {kind} hook {name} did not end within {_options.HookTimeout}; the write was refused and nothing of it was saved.");
        }
        finally
        {
            // Whatever made the caller leave, before its transaction and its catch blocks see that it did.
            if (!running.IsCompleted)
            {
                _guard.MarkLeftRunning(name);
            }

            // What the hook does from here on is not waited for, and a failure of it is not left unobserved.
            if (!running.IsCompletedSuccessfully)
            {
                _ = running.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            var elapsed = _time.GetElapsedTime(started);
            if (elapsed >= _options.SlowHookThreshold && elapsed < _options.HookTimeout)
            {
                ContentLog.SlowHook(_logger, kind, name, type.Name, elapsed);
            }
        }
    }

    private List<BoundHook> Resolve(HookKind kind, SchemaType type)
    {
        var hooks = new List<BoundHook>();
        HookBinder.For(typeof(ContentDocument)).Collect(services, kind, hooks);

        if (type.ClrType is { } clrType)
        {
            var chain = new List<Type>();
            for (var current = clrType; current is not null && current != typeof(object); current = current.BaseType)
            {
                chain.Add(current);
            }

            for (var index = chain.Count - 1; index >= 0; index--)
            {
                HookBinder.For(chain[index]).Collect(services, kind, hooks);
            }
        }

        if (hooks.Count < 2)
        {
            return hooks;
        }

        var tiebreaker = services.GetService(typeof(IHookTiebreaker)) as IHookTiebreaker;
        return [.. hooks
            .OrderBy(hook => Orders.GetOrAdd(hook.ImplementationType, static type => type.GetCustomAttribute<HookOrderAttribute>()?.Order ?? 0))
            .ThenBy(hook => tiebreaker?.Rank(hook.ImplementationType) ?? 0)];
    }

    // The entry as the hooks see it: the document, or the content class mapped from it.
    private sealed class Session(
        Type? clrType,
        OperationContext operation,
        ContentDocument entry,
        ContentDocument? previous,
        IReadOnlyList<string> locales,
        IHookEntryMapper? mapper)
    {
        private object? _previousEntry;
        private IReadOnlyList<string> _changedPaths = ChangedFields(previous, entry);

        public void RefreshChangedPaths() => _changedPaths = ChangedFields(previous, entry);

        // A save always moves the version and the update time, which say nothing about what changed.
        private static List<string> ChangedFields(ContentDocument? previous, ContentDocument entry) =>
            [.. ContentChanges.Between(previous, entry).Where(path => SystemField.Find(path) is null)];

        public HookContext<T> CreateContext<T>(out object? mapped)
        {
            if (typeof(T) == typeof(ContentDocument))
            {
                mapped = null;
                return new HookContext<T>(operation, (T)(object)entry, (T?)(object?)previous, _changedPaths) { Locales = locales };
            }

            var map = RequireMapper();
            mapped = map.ToEntry(clrType!, entry);
            if (previous is not null)
            {
                _previousEntry ??= map.ToEntry(clrType!, previous);
            }

            return new HookContext<T>(operation, (T)mapped, (T?)_previousEntry, _changedPaths) { Locales = locales };
        }

        public void WriteBack(object mapped) => RequireMapper().CopyTo(mapped, entry);

        private IHookEntryMapper RequireMapper() =>
            mapper ?? throw new InvalidOperationException("A hook is registered for a content class, but no IHookEntryMapper is registered to map the entry to it.");
    }

    private abstract class BoundHook(Type implementationType)
    {
        public Type ImplementationType { get; } = implementationType;

        public abstract ValueTask InvokeAsync(Session session, bool mutable, CancellationToken cancellationToken);
    }

    private sealed class BoundHook<T>(Type implementationType, Func<HookContext<T>, CancellationToken, ValueTask> call) : BoundHook(implementationType)
    {
        public override async ValueTask InvokeAsync(Session session, bool mutable, CancellationToken cancellationToken)
        {
            var context = session.CreateContext<T>(out var mapped);
            await call(context, cancellationToken);

            if (context.IsCancelled)
            {
                throw new HookRejectedException(context.CancelCode!, context.CancelMessage!);
            }

            if (mutable && mapped is not null)
            {
                session.WriteBack(mapped);
            }
        }
    }

    private abstract class HookBinder
    {
        private static readonly ConcurrentDictionary<Type, HookBinder> Cache = new();

        public static HookBinder For(Type entryType) =>
            Cache.GetOrAdd(entryType, static type => (HookBinder)Activator.CreateInstance(typeof(HookBinder<>).MakeGenericType(type))!);

        public abstract void Collect(IServiceProvider services, HookKind kind, List<BoundHook> into);
    }

    private sealed class HookBinder<T> : HookBinder
    {
        public override void Collect(IServiceProvider services, HookKind kind, List<BoundHook> into)
        {
            switch (kind)
            {
                case HookKind.BeforeValidate: Add<IBeforeValidateHook<T>>(services, into, hook => hook.BeforeValidateAsync); break;
                case HookKind.BeforeCreate: Add<IBeforeCreateHook<T>>(services, into, hook => hook.BeforeCreateAsync); break;
                case HookKind.AfterCreate: Add<IAfterCreateHook<T>>(services, into, hook => hook.AfterCreateAsync); break;
                case HookKind.BeforeUpdate: Add<IBeforeUpdateHook<T>>(services, into, hook => hook.BeforeUpdateAsync); break;
                case HookKind.AfterUpdate: Add<IAfterUpdateHook<T>>(services, into, hook => hook.AfterUpdateAsync); break;
                case HookKind.BeforeDelete: Add<IBeforeDeleteHook<T>>(services, into, hook => hook.BeforeDeleteAsync); break;
                case HookKind.AfterDelete: Add<IAfterDeleteHook<T>>(services, into, hook => hook.AfterDeleteAsync); break;
                case HookKind.BeforePublish: Add<IBeforePublishHook<T>>(services, into, hook => hook.BeforePublishAsync); break;
                case HookKind.AfterPublish: Add<IAfterPublishHook<T>>(services, into, hook => hook.AfterPublishAsync); break;
                case HookKind.BeforeUnpublish: Add<IBeforeUnpublishHook<T>>(services, into, hook => hook.BeforeUnpublishAsync); break;
                case HookKind.AfterUnpublish: Add<IAfterUnpublishHook<T>>(services, into, hook => hook.AfterUnpublishAsync); break;
                case HookKind.BeforeRestore: Add<IBeforeRestoreHook<T>>(services, into, hook => hook.BeforeRestoreAsync); break;
                case HookKind.AfterRestore: Add<IAfterRestoreHook<T>>(services, into, hook => hook.AfterRestoreAsync); break;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private static void Add<THook>(IServiceProvider services, List<BoundHook> into, Func<THook, Func<HookContext<T>, CancellationToken, ValueTask>> bind)
            where THook : notnull
        {
            if (services.GetService(typeof(IEnumerable<THook>)) is not IEnumerable<THook> hooks)
            {
                return;
            }

            foreach (var hook in hooks)
            {
                into.Add(new BoundHook<T>(hook.GetType(), bind(hook)));
            }
        }
    }
}
