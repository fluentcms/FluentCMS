using System.Text;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Audit;

// A migrated database on one provider, with a clock and an initiator each append sets, the way a run of operations would.
internal sealed class AuditScenario : IAsyncDisposable
{
    public static readonly UserInitiator Amir = new(Guid.NewGuid(), "Amir");

    private readonly TemporaryDatabase _database;
    private readonly ServiceProvider _services;
    private readonly ManualClock _clock = new(DateTimeOffset.UnixEpoch);
    private readonly ActingInitiator _initiator = new();

    private AuditScenario(TemporaryDatabase database)
    {
        _database = database;
        _services = TestServices.Build(database.Configuration, collection =>
        {
            collection.AddSingleton<TimeProvider>(_clock);
            collection.AddSingleton<ICurrentInitiator>(_initiator);
        });
    }

    public static async Task<AuditScenario> CreateAsync(string provider)
    {
        var scenario = new AuditScenario(await TemporaryDatabase.CreateAsync(provider));
        await TestServices.MigrateAsync(scenario._services);
        return scenario;
    }

    public Task<AuditEvent> AppendAsync(DateTimeOffset at, string operation, AuditSubject? subject = null)
    {
        return AppendAsync(at, new NewAuditEvent(operation) { SubjectType = subject?.Type, SubjectId = subject?.Id });
    }

    public async Task<AuditEvent> AppendAsync(DateTimeOffset at, NewAuditEvent entry, Initiator? by = null)
    {
        _clock.Now = at;
        _initiator.Current = by ?? Amir;

        await using var scope = _services.CreateAsyncScope();
        await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();
        var appended = await scope.ServiceProvider.GetRequiredService<IAuditLog>().AppendAsync(entry);
        await work.CommitAsync();

        return appended;
    }

    public async Task<List<AuditEvent>> ReadAsync(AuditQuery query)
    {
        await using var scope = _services.CreateAsyncScope();
        var read = scope.ServiceProvider.GetRequiredService<IAuditReader>().Read(query);
        Assert.True(read.IsSuccess, string.Join("; ", read.Errors.Select(error => error.Message)));

        return await read.Value.ToListAsync();
    }

    // An export records itself as Amir, at the time given or at the time of the last append.
    public async Task<string> ExportAsync(AuditQuery query, AuditExportFormat format, DateTimeOffset? at = null)
    {
        _clock.Now = at ?? _clock.Now;
        _initiator.Current = Amir;

        await using var scope = _services.CreateAsyncScope();
        using var destination = new MemoryStream();
        var exported = await scope.ServiceProvider.GetRequiredService<IAuditExporter>().ExportAsync(query, format, destination);
        Assert.True(exported.IsSuccess, string.Join("; ", exported.Errors.Select(error => error.Message)));

        return Encoding.UTF8.GetString(destination.ToArray());
    }

    public AsyncServiceScope CreateScope()
    {
        return _services.CreateAsyncScope();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _database.DisposeAsync();
    }

    private sealed class ActingInitiator : ICurrentInitiator
    {
        public Initiator? Current { get; set; }
    }
}
