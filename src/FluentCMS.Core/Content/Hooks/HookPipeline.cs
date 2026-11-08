using System.Collections.Concurrent;
using System.Reflection;
using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Telemetry;
using FluentCMS.Hooks;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Hooks;

// Runs the hooks of one kind for one entry. Hooks for ContentDocument (every type) and for the content class of the type,
// and for its base classes, form one sequence ordered by [HookOrder], then by IHookTiebreaker rank. Hooks that still tie keep
// the order of resolution: all-types hooks first, then base classes before derived ones, each in registration order.
// The first hook that cancels throws HookRejectedException and the hooks after it do not run.
// The changed paths are those between previous and entry, computed again before each before hook so that a hook sees the
// changes of the hooks before it; an operation that changes no field passes the stored state as previous.
// Only before hooks change the entry: an after hook works on a copy. A hook of a plugin disabled in the operation's site does not run.
public sealed class HookPipeline(IServiceProvider services)
{
    private static readonly ConcurrentDictionary<Type, int> Orders = new();

    // The hooks of a scope do not change, so each kind is resolved and ordered once per content class and scope, not on every
    // call. A hook registered as transient therefore lives as long as the scope, as a scoped one does.
    private readonly Dictionary<(HookKind Kind, Type? ClrType), List<BoundHook>> _resolved = [];

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
            if (mutable && !first)
            {
                session.RefreshChangedPaths();
            }

            first = false;

            await hook.InvokeAsync(session, mutable, cancellationToken);
        }

        return true;
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
