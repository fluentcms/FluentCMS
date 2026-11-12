using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FluentCMS.Core.Storage;

// The migrations that one migrator applied. Two processes that migrate one database at once both find a migration
// pending; EF's migrations lock lets one apply it, and the other must not report it as its own. EF names each migration
// it applies in an event of its diagnostic listener, with the migrator that applies it.
internal sealed class AppliedMigrations : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private static readonly string Applying = RelationalEventId.MigrationApplying.Name!;

    private readonly IMigrator _migrator;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly List<string> _ids = [];
    private readonly Lock _gate = new();

    private AppliedMigrations(IMigrator migrator) => _migrator = migrator;

    public IReadOnlyList<string> Ids
    {
        get
        {
            lock (_gate)
            {
                return [.. _ids];
            }
        }
    }

    public static AppliedMigrations Record(IMigrator migrator)
    {
        var recorder = new AppliedMigrations(migrator);
        recorder.Add(DiagnosticListener.AllListeners.Subscribe(recorder));
        return recorder;
    }

    public void OnNext(DiagnosticListener value)
    {
        if (value.Name == DbLoggerCategory.Name)
        {
            Add(value.Subscribe(this, name => name == Applying));
        }
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (value.Key == Applying && value.Value is MigrationEventData data && ReferenceEquals(data.Migrator, _migrator)
            && data.Migration.GetType().GetCustomAttribute<MigrationAttribute>()?.Id is { } id)
        {
            lock (_gate)
            {
                _ids.Add(id);
            }
        }
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }
    }

    private void Add(IDisposable subscription)
    {
        lock (_gate)
        {
            _subscriptions.Add(subscription);
        }
    }
}
