using FluentCMS.Content;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Activity;
using FluentCMS.Data.Tests.Events;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Events;
using FluentCMS.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Settings.SettingsDatabase;

namespace FluentCMS.Data.Tests.Settings;

public sealed class SettingsEventTests
{
    private const string Original = """{"provider":"old"}""";
    private const string Package = """{"provider":"new","apiKey":"ignored-package-secret"}""";

    [Theory]
    [InlineData("insert")]
    [InlineData("update")]
    [InlineData("reset")]
    [InlineData("import")]
    public async Task ARejectedOutboxInsertRollsBackTheSettingsChange(string change)
    {
        var faults = new JobFaults();
        using var database = new SettingsDatabase(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(faults)));
        if (change != "insert")
        {
            database.AddRow(SiteA.Value, "media", Original);
        }

        var before = database.Rows().Select(row => (row.Id, row.Version, row.Json)).ToArray();
        var cache = database.Inner.Database.Services.GetRequiredService<SettingsCache>();
        var generation = cache.Generation("media");
        faults.Fail(command => TestProvider.IsInsertInto(command.CommandText, "cms_outbox"));

        await Should.ThrowAsync<DbUpdateException>(() => change switch
        {
            "reset" => database.ServiceAsync(async (service, descriptor) =>
            {
                await service.ResetAsync(descriptor, SiteA, 1, Token);
                return true;
            }),
            "import" => ImportAsync(database),
            _ => database.SetAsync(SiteA, new MediaSettings { Provider = "new" }),
        });

        database.Rows().Select(row => (row.Id, row.Version, row.Json)).ShouldBe(before);
        database.Inner.Rows().ShouldBeEmpty();
        database.Seen.Events.ShouldBeEmpty();
        cache.Generation("media").ShouldBe(generation);
    }

    [Fact]
    public async Task AnImportInterruptedAfterCommitAndBeforeDispatchLeavesOneDeliverableEvent()
    {
        using var database = new SettingsDatabase(cms => cms.Services.AddSingleton<ILogger<SettingsService>, StopBeforeDispatch>());

        await Should.ThrowAsync<InterruptedException>(() => ImportAsync(database));

        var setting = database.Rows().ShouldHaveSingleItem();
        (setting.SiteId, setting.UpdatedBy, setting.Version).ShouldBe((SiteA.Value, SettingsDatabase.Admin.Id, 1));
        StoredJson.Parse(setting.Json)["provider"]!.GetValue<string>().ShouldBe("new");
        database.Seen.Events.ShouldBeEmpty();
        var pending = database.Inner.Rows().ShouldHaveSingleItem();
        pending.Status.ShouldBe(OutboxStatus.Pending);
        pending.EventType.ShouldBe(typeof(SettingsChangedEvent).FullName);
        pending.Payload.ShouldNotContain("ignored-package-secret");

        (await database.Inner.DrainAsync()).ShouldBe(1);
        (await database.Inner.DrainAsync()).ShouldBe(0);
        var activity = database.Inner.ActivityRows().ShouldHaveSingleItem();
        (activity.Action, activity.TargetId, activity.SiteId, activity.ActorId, activity.Origin)
            .ShouldBe(("settings.changed", "media", SiteA.Value, SettingsDatabase.Admin.Id, Origin.Import));
    }

    [Fact]
    public async Task DryAndSkippedImportsNeitherWriteNorRaiseAnEvent()
    {
        using var database = new SettingsDatabase();

        (await ImportAsync(database, dryRun: true)).ShouldBe(TransferOutcome.Created);
        database.Rows().ShouldBeEmpty();
        database.Inner.Rows().ShouldBeEmpty();
        database.Seen.Events.ShouldBeEmpty();

        (await ImportAsync(database)).ShouldBe(TransferOutcome.Created);
        var before = database.Rows().Single();
        var heard = database.Seen.Of<SettingsChangedEvent>().ShouldHaveSingleItem();
        (heard.Source.ActorId, heard.Source.SiteId, heard.Source.Origin).ShouldBe((SettingsDatabase.Admin.Id, SiteA.Value, Origin.Import));
        (await ImportAsync(database)).ShouldBe(TransferOutcome.Skipped);
        var after = database.Rows().ShouldHaveSingleItem();
        (after.Id, after.Version, after.Json).ShouldBe((before.Id, before.Version, before.Json));
        database.Inner.Rows().ShouldHaveSingleItem().EventId.ShouldBe(heard.EventId);
        database.Seen.Events.ShouldHaveSingleItem();
    }

    private static Task<TransferOutcome> ImportAsync(SettingsDatabase database, bool dryRun = false) =>
        database.AsAsync(SettingsDatabase.Admin, services =>
        {
            var accessor = services.GetRequiredService<IOperationContextAccessor>();
            var caller = accessor.Current!;
            accessor.Current = caller with { Principal = Principal.System, Actor = caller.AuditPrincipal, Origin = Origin.Import };
            return services.GetRequiredService<SettingsService>().ImportAsync("media", SiteA, Package, dryRun, caller, Token);
        });

    private sealed class InterruptedException : Exception;

    // Interrupt at the change notification, before in-process handlers or the request for a drain can run.
    private sealed class StopBeforeDispatch : ILogger<SettingsService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Name == nameof(SettingsLog.Changed))
            {
                throw new InterruptedException();
            }
        }
    }
}
