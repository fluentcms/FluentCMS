using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Events;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Publishing;
using FluentCMS.Sites;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

public sealed class TrashPurgeJobTests
{
    private static readonly DateTimeOffset Long = DateTimeOffset.UtcNow.AddDays(-90);

    private sealed class Poisoned : IEntryChangeStager
    {
        public HashSet<Guid> Ids { get; } = [];

        public Guid Id
        {
            set
            {
                Ids.Clear();
                Ids.Add(value);
            }
        }

        public ValueTask StageAsync(DbContext context, IReadOnlyList<EntryChange> changes, CancellationToken cancellationToken) =>
            changes.Any(change => change.Kind == EntryChangeKind.Purged && Ids.Contains(change.Id)) ? throw new InvalidOperationException("The outbox is down.") : ValueTask.CompletedTask;
    }

    private sealed class Heard(Seen seen) : IEventHandler<EntryPurgedEvent>
    {
        public ValueTask HandleAsync(EntryPurgedEvent message, CancellationToken cancellationToken) => seen.HeardAsync("purged", message, null, cancellationToken);
    }

    private static async Task RunAsync(ServiceDatabase database, int limit = 500, int pageSize = 100)
    {
        using var scope = database.Database.NewScope();
        var context = new OperationContext(Principal.System, Origin.Job, SiteA, "en", null, "test", DateTimeOffset.UtcNow);
        await scope.ServiceProvider.GetRequiredService<TrashPurgeJob>().ExecuteAsync(new TrashPurgePayload(limit, pageSize), new JobContext(Guid.NewGuid(), TrashPurgeJob.Type, "default", 1, 3, context, Token));
    }

    private static async Task<Guid> TrashedAsync(ServiceDatabase database, string title, DateTimeOffset at, string type = "article", SiteId? site = null)
    {
        var entry = type == "article" ? new ContentDocument { ["title"] = title } : new ContentDocument { ["name"] = title };
        var id = IdOf(await database.CreateAsync(Editor, entry, type, site: site));
        await database.DeleteInAsync(Editor, id, site ?? SiteA, type);
        database.TrashedAt(id, at);
        return id;
    }

    private static bool Exists(ServiceDatabase database, Guid id) =>
        database.Read(context => context.Documents.IgnoreQueryFilters().Any(document => document.DocumentId == id));

    [Fact]
    public async Task TheJobPurgesOnlyWhatIsPastItsRetentionAndLogsAReport()
    {
        var logs = new RecordingLogs();
        var seen = new Seen();
        using var database = new ServiceDatabase(cms =>
        {
            cms.Services.AddSingleton<ILoggerProvider>(logs);
            cms.Services.AddSingleton(seen);
            cms.Events.AddHandler<EntryPurgedEvent, Heard>();
        });
        var old = await TrashedAsync(database, "Old", DateTimeOffset.UtcNow.AddDays(-31));
        var recent = await TrashedAsync(database, "Recent", DateTimeOffset.UtcNow.AddDays(-29));
        var fresh = await TrashedAsync(database, "Fresh", DateTimeOffset.UtcNow);
        var live = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Live" }));

        await RunAsync(database);

        Exists(database, old).ShouldBeFalse();
        database.Held(old).ShouldBe(TrashDatabase.Nothing);
        new[] { recent, fresh, live }.ShouldAllBe(id => Exists(database, id));
        (await database.TrashAsync(Editor)).Items.Select(entry => (Guid)entry["id"]!).ShouldBe([recent, fresh], ignoreOrder: true);
        logs.Count("Report").ShouldBe(1);
        logs.Count("TypeReport").ShouldBe(1);
        seen.Of<EntryPurgedEvent>().ShouldHaveSingleItem().EntryId.ShouldBe(old);

        await RunAsync(database);

        logs.Count("Report").ShouldBe(2);
        logs.Count("TypeReport").ShouldBe(1);
        seen.Of<EntryPurgedEvent>().Count().ShouldBe(1);
        (await database.RestoreAsync(Editor, recent))["title"].ShouldBe("Recent");
    }

    [Fact]
    public async Task EachTypeKeepsItsTrashForItsOwnPeriod()
    {
        var model = SampleSchema.NewBuilder()
            .AddCollection("note", type => type.PublishPolicy(UnpublishedReferencePolicy.Warn, PublishPolicyAttribute.DefaultMaxVersions, 3).Field("name", "text"))
            .AddCollection("memo", type => type.Field("name", "text"))
            .Build();
        using var database = new ServiceDatabase(schema: model);
        var shortNote = await TrashedAsync(database, "Note", DateTimeOffset.UtcNow.AddDays(-4), "note");
        var freshNote = await TrashedAsync(database, "Note", DateTimeOffset.UtcNow.AddDays(-2), "note");
        var memo = await TrashedAsync(database, "Memo", DateTimeOffset.UtcNow.AddDays(-4), "memo");
        var oldMemo = await TrashedAsync(database, "Memo", DateTimeOffset.UtcNow.AddDays(-31), "memo");

        await RunAsync(database);

        Exists(database, shortNote).ShouldBeFalse();
        Exists(database, oldMemo).ShouldBeFalse();
        Exists(database, freshNote).ShouldBeTrue();
        Exists(database, memo).ShouldBeTrue();
        model.FindContentType("note")!.TrashRetentionDays.ShouldBe(3);
        model.FindContentType("memo")!.TrashRetentionDays.ShouldBe(30);
    }

    [Fact]
    public async Task TheJobPurgesEverySiteAndEveryVersionOfWhatItTakes()
    {
        using var database = new ServiceDatabase();
        var inA = await TrashedAsync(database, "In A", Long);
        var inB = await TrashedAsync(database, "In B", Long, site: SiteB);
        var published = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Published" }));
        await database.PublishAsync(Editor, published);
        await database.PatchAsync(Editor, published, new() { ["title"] = "Changed" }, 1);
        await database.UpdateAsync(Editor, published, new() { ["title"] = "Bonjour" }, 0, locale: "fr");
        await database.DeleteAsync(Editor, published);
        database.TrashedAt(published, Long);

        await RunAsync(database);

        new[] { inA, inB, published }.ShouldAllBe(id => database.Held(id) == TrashDatabase.Nothing);
    }

    [Fact]
    public async Task AnEntryThatCannotBePurgedStaysAndDoesNotStopTheOthers()
    {
        var poison = new Poisoned();
        var logs = new RecordingLogs();
        using var database = new ServiceDatabase(cms =>
        {
            cms.Services.AddSingleton<ILoggerProvider>(logs);
            cms.Services.AddScoped<IEntryChangeStager>(_ => poison);
        });
        var stuck = await TrashedAsync(database, "Stuck", Long);
        var other = await TrashedAsync(database, "Other", Long);
        poison.Id = stuck;
        var before = database.Trace(stuck);

        await RunAsync(database);

        database.Trace(stuck).ShouldBe(before);
        Exists(database, other).ShouldBeFalse();
        logs.Count("PurgeFailed").ShouldBe(1);

        poison.Id = Guid.Empty;
        await RunAsync(database);

        Exists(database, stuck).ShouldBeFalse();
    }

    [Fact]
    public async Task EntriesThatKeepFailingDoNotStarveTheOnesBehindThem()
    {
        var poison = new Poisoned();
        using var database = new ServiceDatabase(cms => cms.Services.AddScoped<IEntryChangeStager>(_ => poison));
        var stuck = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            stuck.Add(await TrashedAsync(database, $"Stuck {index}", DateTimeOffset.UtcNow.AddDays(-100 + index)));
        }

        var good = new List<Guid>
        {
            await TrashedAsync(database, "Good one", DateTimeOffset.UtcNow.AddDays(-50)),
            await TrashedAsync(database, "Good two", DateTimeOffset.UtcNow.AddDays(-40)),
        };
        poison.Ids.UnionWith(stuck);

        await RunAsync(database, limit: 2, pageSize: 2);

        good.ShouldAllBe(id => !Exists(database, id));
        stuck.ShouldAllBe(id => Exists(database, id));
    }

    [Fact]
    public async Task AnEntryPutInTheTrashAgainAfterItWasListedAsDueIsNotPurged()
    {
        using var database = new ServiceDatabase();
        var id = await TrashedAsync(database, "Again", DateTimeOffset.UtcNow);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        var before = database.Trace(id);

        Task PurgeBeforeAsync(DateTimeOffset at) => database.AsAsync<object?>(
            Principal.System,
            async services =>
            {
                await services.GetRequiredService<ContentService>().PurgeAsync("article", id, at, Token);
                return null;
            },
            origin: Origin.Job);

        await Should.ThrowAsync<EntryNotFoundException>(() => PurgeBeforeAsync(cutoff));
        database.Trace(id).ShouldBe(before);
        database.Held(id).ShouldNotBe(TrashDatabase.Nothing);

        await PurgeBeforeAsync(DateTimeOffset.UtcNow.AddMinutes(1));

        database.Held(id).ShouldBe(TrashDatabase.Nothing);
    }

    [Fact]
    public async Task OneTypeThatFailsDoesNotStopTheOthers()
    {
        var model = SampleSchema.NewBuilder()
            .AddCollection("note", type => type.PublishPolicy(UnpublishedReferencePolicy.Warn, PublishPolicyAttribute.DefaultMaxVersions, PublishPolicyAttribute.MaxTrashRetentionDays).Field("name", "text"))
            .AddCollection("memo", type => type.Field("name", "text"))
            .Build();
        var faults = new JobFaults();
        var logs = new RecordingLogs();
        using var database = new ServiceDatabase(
            cms =>
            {
                cms.Services.AddSingleton<ILoggerProvider>(logs);
                cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(faults));
            },
            model);
        var note = await TrashedAsync(database, "Note", Long, "note");
        var memo = await TrashedAsync(database, "Memo", Long, "memo");
        faults.Fail(command => command.CommandText.Contains("cms_document", StringComparison.Ordinal)
            && command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
            && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, "note")));

        await RunAsync(database);

        Exists(database, memo).ShouldBeFalse();
        Exists(database, note).ShouldBeTrue();
        logs.Count("TypeFailed").ShouldBe(1);
        logs.Count("Report").ShouldBe(1);

        faults.Recover();
        await RunAsync(database);

        Exists(database, note).ShouldBeTrue();
    }

    [Fact]
    public async Task TheLongestRetentionStillWorksOutAMoment()
    {
        var model = SampleSchema.NewBuilder()
            .AddCollection("note", type => type.PublishPolicy(UnpublishedReferencePolicy.Warn, PublishPolicyAttribute.DefaultMaxVersions, PublishPolicyAttribute.MaxTrashRetentionDays).Field("name", "text"))
            .AddCollection("memo", type => type.Field("name", "text"))
            .Build();
        using var database = new ServiceDatabase(schema: model);
        var note = await TrashedAsync(database, "Note", Long, "note");
        var memo = await TrashedAsync(database, "Memo", Long, "memo");

        await RunAsync(database);

        Exists(database, note).ShouldBeTrue();
        Exists(database, memo).ShouldBeFalse();
    }

    [Fact]
    public async Task TheLimitCapsWhatOneRunPurgesAndTheNextTakesTheRest()
    {
        using var database = new ServiceDatabase();
        var ids = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            ids.Add(await TrashedAsync(database, $"Old {index}", Long));
        }

        await RunAsync(database, limit: 2);
        ids.Count(id => Exists(database, id)).ShouldBe(1);

        await RunAsync(database, limit: 2);
        ids.Count(id => Exists(database, id)).ShouldBe(0);
    }

    private sealed class FlakyScheduler : IJobScheduler
    {
        public int Failures { get; set; }

        public int Added { get; private set; }

        public Task<Guid> EnqueueAsync<TJob, TPayload>(TPayload payload, JobOptions? options = null, CancellationToken cancellationToken = default)
            where TJob : class, IJob<TPayload> => throw new NotSupportedException();

        public Task<Guid> ScheduleAsync<TJob, TPayload>(TPayload payload, DateTimeOffset runAt, JobOptions? options = null, CancellationToken cancellationToken = default)
            where TJob : class, IJob<TPayload> => throw new NotSupportedException();

        public Task AddRecurringAsync<TJob, TPayload>(string name, string cron, TPayload payload, string? queue, CancellationToken cancellationToken = default)
            where TJob : class, IJob<TPayload>
        {
            if (Failures > 0)
            {
                Failures--;
                throw new InvalidOperationException("The database is down.");
            }

            Added++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AScheduleThatCannotBeStoredAtStartupIsRetriedUntilItIs()
    {
        var scheduler = new FlakyScheduler { Failures = 2 };
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var service = new TrashPurgeScheduleService(scheduler, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<TrashPurgeScheduleService>.Instance);

        await service.StartAsync(Token);
        scheduler.Added.ShouldBe(0);

        await Eventually.CompletesAsync(service.ExecuteTask!, () => clock.Advance(TimeSpan.FromSeconds(30)));
        await service.StopAsync(Token);

        scheduler.Added.ShouldBe(1);
    }

    [Fact]
    public async Task TheJobRunsThroughTheRunnerAndItsScheduleIsStoredOnceHoweverOftenItStarts()
    {
        using var database = new ServiceDatabase();
        var old = await TrashedAsync(database, "Old", Long);
        var scheduler = database.Database.Services.GetRequiredService<IJobScheduler>();
        var id = await scheduler.EnqueueAsync<TrashPurgeJob, TrashPurgePayload>(new TrashPurgePayload(), cancellationToken: Token);

        (await database.Database.Services.GetRequiredService<JobExecutor>().RunNextAsync(JobRunnerOptions.DefaultQueue, "worker", Token)).ShouldBeTrue();

        Exists(database, old).ShouldBeFalse();
        database.Database.Services.GetRequiredService<SystemDbContext>().Set<Job>().Single(job => job.Id == id).Status.ShouldBe(JobStatus.Succeeded);

        var service = database.Database.Services.GetServices<IHostedService>().OfType<TrashPurgeScheduleService>().Single();
        for (var start = 0; start < 2; start++)
        {
            await service.StartAsync(Token);
            await service.StopAsync(Token);
        }

        var recurring = database.Database.Services.GetRequiredService<SystemDbContext>().Set<RecurringJob>().AsNoTracking().Single(job => job.Name == TrashPurgeJob.RecurringName);
        recurring.Type.ShouldBe(TrashPurgeJob.Type);
        recurring.Cron.ShouldBe(TrashPurgeJob.Cron);
        recurring.Enabled.ShouldBeTrue();
    }
}
