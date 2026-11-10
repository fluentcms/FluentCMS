using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.ActivityLog;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.Tests.Events;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Activity.ActivityFixture;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Activity;

// The log is written for the events of the catalogue without anyone remembering to, says who did it and from where, and
// holds nothing a reader of the log could not otherwise see.
public sealed class ActivityLogTests
{
    private static readonly Guid Entry = Guid.Parse("00000000-0000-0000-0000-00000000e001");

    private static readonly Guid Job = Guid.Parse("00000000-0000-0000-0000-00000000f001");

    // One of every event of the catalogue. A new event fails the first test until it has a sample here and an entry in ActivityEvents.
    private static readonly CmsEvent[] Samples =
    [
        new EntryDraftSavedEvent("article", Entry, "en", 1),
        new EntryPublishedEvent("article", Entry, "en", 1),
        new EntryUnpublishedEvent("article", Entry, "en", 1),
        new EntryDraftDiscardedEvent("article", Entry, "en", 1),
        new EntryVersionRestoredEvent("article", Entry, "en", 2),
        new EntryTrashedEvent("article", Entry, "en", 1),
        new EntryRestoredEvent("article", Entry, "en", 1),
        new EntryPurgedEvent("article", Entry, "en", 1),
        new EntryScheduledEvent("article", Entry, "en", 1, new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero), "publish"),
        new EntryScheduleCancelledEvent("article", Entry, "en", 1, "publish"),
        new SettingsChangedEvent("seo"),
        new SiteCreatedEvent(SiteB.Value, "b"),
        new SiteUpdatedEvent(SiteB.Value, "b"),
        new PluginEnabledEvent("media", SiteB.Value),
        new PluginDisabledEvent("media", SiteB.Value),
        new UserCreatedEvent(Entry, "ann"),
        new UserUpdatedEvent(Entry, "ann", ["email"]),
        new UserDeactivatedEvent(Entry, "ann"),
        new UserRoleChangedEvent(Entry, "ann", [Job], []),
        new UserLoginLinkedEvent(Entry, "ann", "corp"),
        new UserLoginUnlinkedEvent(Entry, "ann", "corp"),
        new UserTwoFactorResetEvent(Entry, "ann"),
        new LoginSucceededEvent(Entry, "ann", LoginMethods.Password, "10.0.0.7"),
        new LoginFailedEvent(null, null, LoginMethods.Password, LoginFailureReasons.UnknownUser, "10.0.0.8"),
        new ApiKeyCreatedEvent(Entry, "site key", "delivery"),
        new ApiKeyRevokedEvent(Entry, "site key"),
        new ApiKeyExpiryChangedEvent(Entry, "site key", new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero)),
        new ApiKeyRotatedEvent(Entry, "site key", new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero)),
        new ApiKeyAllowedAddressesChangedEvent(Entry, "site key", ["203.0.113.0/24"]),
        new ApiKeyAddressRefusedEvent(Entry, "site key", "198.51.100.7"),
        new CacheInvalidatedEvent(["article:1", "article:2"]),
        new JobFailedEvent(Job, "events.outbox.drain", "outbox", 1, 3, "boom"),
        new JobDeadLetteredEvent(Job, "events.outbox.drain", "outbox", 3, "boom"),
    ];

    private static Task<bool> PublishAsync(ServiceDatabase database, Principal principal, CmsEvent @event, SiteId? site = null, Origin origin = Origin.Mcp) =>
        database.AsAsync(principal, async services =>
        {
            await services.GetRequiredService<IEventBus>().PublishAsync(@event, Token);
            return true;
        }, site: site, origin: origin);

    [Fact]
    public void EveryEventOfTheCatalogueHasASampleHere()
    {
        // The media plugin logs its own events, and its tests cover them.
        var catalogue = typeof(CmsEvent).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(CmsEvent)) && !type.IsAssignableTo(typeof(MediaAssetEvent)));

        catalogue.ShouldBe(Samples.Select(sample => sample.GetType()), ignoreOrder: true);
    }

    [Fact]
    public async Task EveryEventOfTheCatalogueProducesARowWithItsOriginActorClientAndCorrelationId()
    {
        using var database = new ServiceDatabase(On());

        foreach (var sample in Samples)
        {
            await PublishAsync(database, Editor, sample, SiteB);
        }

        (await database.DrainAsync()).ShouldBe(Samples.Length);

        var rows = database.ActivityRows();
        rows.Count.ShouldBe(Samples.Length);
        rows.Select(row => row.Action).Distinct().Count().ShouldBe(Samples.Length);
        rows.ShouldAllBe(row => row.ActorId == Editor.Id && row.ActorType == Editor.Kind && row.ActorName == Editor.Name);
        rows.ShouldAllBe(row => row.Origin == Origin.Mcp && row.ClientId == "client" && row.CorrelationId == "correlation");
        rows.ShouldAllBe(row => row.SiteId == SiteB.Value);
    }

    [Fact]
    public async Task ARowSaysWhatTheEventSaysEvenThoughAJobDeliversIt()
    {
        using var database = new ServiceDatabase(On());
        var occurredAt = new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.FromHours(2));
        var source = new EventSource(occurredAt, SiteB.Value, Guid.Parse("00000000-0000-0000-0000-0000000000f1"), "Importer", PrincipalKind.ServiceAccount, Origin.Cli, "import-1", "key-7");

        await PublishAsync(database, Editor, new EntryPublishedEvent("article", Entry, "fr", 4) { Source = source });
        await database.DrainAsync();

        var row = database.ActivityRows().ShouldHaveSingleItem();
        row.Action.ShouldBe("content.published");
        row.OccurredAt.ShouldBe(occurredAt);
        row.ActorType.ShouldBe(PrincipalKind.ServiceAccount);
        row.ActorId.ShouldBe(source.ActorId);
        row.ActorName.ShouldBe("Importer");
        row.Origin.ShouldBe(Origin.Cli);
        row.ClientId.ShouldBe("key-7");
        row.CorrelationId.ShouldBe("import-1");
        row.SiteId.ShouldBe(SiteB.Value);
        row.TargetType.ShouldBe("article");
        row.TargetId.ShouldBe(Entry.ToString("D"));
        row.Locale.ShouldBe("fr");
        ActivityText.ReadDetails(row.Details)["version"].ShouldBe("4");
    }

    [Fact]
    public async Task AnEventOfNoSiteOrActorIsAnInstanceRowOfTheSystem()
    {
        using var database = new ServiceDatabase(On());

        using (var scope = database.Database.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(new JobFailedEvent(Job, "x", "default", 1, 3, "boom"), Token);
        }

        await database.DrainAsync();

        var row = database.ActivityRows().ShouldHaveSingleItem();
        row.SiteId.ShouldBeNull();
        row.ActorType.ShouldBe(PrincipalKind.System);
        row.ActorId.ShouldBe(Guid.Empty);
        row.Origin.ShouldBe(Origin.Job);
        row.TargetType.ShouldBe("job");
        row.TargetLabel.ShouldBe("x");
    }

    [Fact]
    public async Task TheWritesOfContentLeaveTheRowsOfTheirTransitionsAndNoFieldValue()
    {
        using var database = new ServiceDatabase(On());

        var entry = await database.CreateAsync(Editor, new() { ["title"] = "Confidential headline", ["subtitle"] = "Unannounced product" });
        var id = IdOf(entry);
        await database.AsAsync(Editor, async services =>
        {
            await services.GetRequiredService<IContentService>().PublishAsync("article", id, cancellationToken: Token);
            return true;
        });
        await database.DrainAsync();

        var rows = database.ActivityRows();
        rows.Select(row => row.Action).ShouldBe(["content.draft_saved", "content.published"], ignoreOrder: true);
        rows.ShouldAllBe(row => row.TargetType == "article" && row.TargetId == id.ToString("D") && row.Locale == "en" && row.SiteId == SiteA.Value);

        var everything = string.Join('|', rows.Select(row => $"{row.TargetLabel}{row.Details}{row.TargetType}{row.TargetId}{row.Action}"));
        everything.ShouldNotContain("Confidential");
        everything.ShouldNotContain("Unannounced");
        rows.ShouldAllBe(row => row.TargetLabel == null);
    }

    [Fact]
    public async Task TrashingRestoringAndPurgingAnEntryLeaveTheirRowsAndNoFieldValue()
    {
        using var database = new ServiceDatabase(On());
        var admin = WithRole(BuiltInRoles.SuperAdmin);
        var id = IdOf(await database.CreateAsync(admin, new() { ["title"] = "Confidential headline" }));

        await database.DeleteAsync(admin, id);
        await database.RestoreAsync(admin, id);
        await database.DeleteAsync(admin, id);
        await database.PurgeAsync(admin, id);
        await database.DrainAsync();

        var rows = database.ActivityRows();
        rows.Select(row => row.Action).Order().ShouldBe(["content.draft_saved", "content.purged", "content.restored", "content.trashed", "content.trashed"]);
        rows.ShouldAllBe(row => row.TargetType == "article" && row.TargetId == id.ToString("D") && row.Locale == "en" && row.ActorId == admin.Id);
        rows.Where(row => row.Action is "content.trashed" or "content.restored" or "content.purged").ShouldAllBe(row => ActivityText.ReadDetails(row.Details).ContainsKey("version"));
        string.Join('|', rows.Select(row => $"{row.TargetLabel}{row.Details}")).ShouldNotContain("Confidential");
    }

    [Fact]
    public async Task ThePurgeJobLeavesARowOfThePurgeForTheSystemThatRanIt()
    {
        using var database = new ServiceDatabase(On());
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Old news" }));
        await database.DeleteAsync(Editor, id);
        database.TrashedAt(id, DateTimeOffset.UtcNow.AddDays(-90));

        using (var scope = database.Database.NewScope())
        {
            var operation = new OperationContext(Principal.System, Origin.Job, SiteA, "en", null, "purge-run", DateTimeOffset.UtcNow);
            await scope.ServiceProvider.GetRequiredService<TrashPurgeJob>().ExecuteAsync(new TrashPurgePayload(500, 100), new JobContext(Guid.NewGuid(), TrashPurgeJob.Type, "default", 1, 3, operation, Token));
        }

        await database.DrainAsync();

        var purged = database.ActivityRows().Single(row => row.Action == "content.purged");
        purged.TargetType.ShouldBe("article");
        purged.TargetId.ShouldBe(id.ToString("D"));
        purged.Locale.ShouldBe("en");
        purged.ActorType.ShouldBe(PrincipalKind.System);
        purged.Origin.ShouldBe(Origin.Job);
        purged.CorrelationId.ShouldBe("purge-run");
        purged.SiteId.ShouldBe(SiteA.Value);
        (purged.Details ?? "").ShouldNotContain("Old news");
    }

    [Fact]
    public async Task TheErrorOfAJobIsScrubbedBeforeItIsStoredAsADetail()
    {
        using var database = new ServiceDatabase(On());
        const string error = "POST https://hooks.example.com/services/T00/B00/XXXSECRETPATH failed: token=abc123 Bearer abcdefgh12345678 rejected";

        await PublishAsync(database, Editor, new JobFailedEvent(Job, "webhooks.deliver", "default", 1, 3, error));
        await database.DrainAsync();

        var stored = ActivityText.ReadDetails(database.ActivityRows().Single().Details)["error"].ShouldNotBeNull();
        stored.ShouldNotContain("XXXSECRETPATH");
        stored.ShouldNotContain("abc123");
        stored.ShouldNotContain("abcdefgh12345678");
        stored.ShouldContain("hooks.example.com");
    }

    [Fact]
    public async Task ADetailNamedLikeASecretLosesItsValueAndTheOthersAreScrubbedAndCut()
    {
        using var database = new ServiceDatabase(On());

        await database.AsAsync(Editor, async services =>
        {
            await services.GetRequiredService<IActivityLog>().WriteAsync(
                new ActivityEntry("plugin.configured")
                {
                    TargetType = "plugin",
                    TargetId = "media",
                    TargetLabel = "https://user:pass@cdn.example.com/path?sig=1234",
                    Details = new Dictionary<string, string?>
                    {
                        ["apiKey"] = "k-123456",
                        ["Password"] = "hunter22",
                        ["note"] = "ok password=hunter22 done",
                        ["long"] = new string('x', 1000),
                        ["none"] = null,
                    },
                },
                Token);
            return true;
        });

        var row = database.ActivityRows().Single();
        row.TargetLabel.ShouldBe("https://cdn.example.com");
        var details = ActivityText.ReadDetails(row.Details);
        details["apiKey"].ShouldBe("***");
        details["Password"].ShouldBe("***");
        details["note"].ShouldBe("ok password=*** done");
        details["long"]!.Length.ShouldBe(256);
        details["none"].ShouldBeNull();
        (row.Details ?? "").ShouldNotContain("hunter22");
    }

    [Fact]
    public async Task AWriteSavesNothingTheCallerTrackedAndKeepsItTracked()
    {
        using var database = new ServiceDatabase(On());

        using var scope = database.Database.NewScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var site = await context.Set<Site>().SingleAsync(row => row.Id == SiteA.Value, Token);
        site.Name = "Renamed";

        await scope.ServiceProvider.GetRequiredService<IActivityLog>().WriteAsync(new ActivityEntry("cli.ran"), Token);

        context.Entry(site).State.ShouldBe(EntityState.Modified);
        using var other = database.Database.NewScope();
        (await other.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Site>().AsNoTracking().SingleAsync(row => row.Id == SiteA.Value, Token)).Name.ShouldBe("a");
        database.ActivityRows().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AWriteInsideATransactionIsRefused()
    {
        using var database = new ServiceDatabase(On());

        using var scope = database.Database.NewScope();
        await using var transaction = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Database.BeginTransactionAsync(Token);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () => await scope.ServiceProvider.GetRequiredService<IActivityLog>().WriteAsync(new ActivityEntry("cli.ran"), Token));

        exception.Message.ShouldStartWith("FCMS2704");
        database.ActivityRows().ShouldBeEmpty();
    }

    [Fact]
    public async Task ACutThroughASurrogatePairBacksOffOneCharacter()
    {
        using var database = new ServiceDatabase(On());
        var label = new string('a', 255) + "\U0001F600";

        using (var scope = database.Database.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<IActivityLog>().WriteAsync(new ActivityEntry("cli.ran") { TargetLabel = label, Details = new Dictionary<string, string?> { ["d"] = new string('b', 255) + "\U0001F600" } }, Token);
        }

        var row = database.ActivityRows().Single();
        row.TargetLabel.ShouldBe(new string('a', 255));
        ActivityText.ReadDetails(row.Details)["d"].ShouldBe(new string('b', 255));
    }

    [Fact]
    public async Task AnActionIsStoredInLowerCase()
    {
        using var database = new ServiceDatabase(On());

        using (var scope = database.Database.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<IActivityLog>().WriteAsync(new ActivityEntry("Plugin.Configured"), Token);
        }

        database.ActivityRows().Single().Action.ShouldBe("plugin.configured");
    }

    [Fact]
    public async Task RowsStillWaitingWhenTheLogIsTurnedOffAreDeliveredAndDropped()
    {
        using var database = new ServiceDatabase(On());
        await database.CreateAsync(Editor, new() { ["title"] = "T" });

        using var off = database.Database.NewInstance();
        (await off.GetRequiredService<OutboxDrainer>().DrainAsync(Token)).ShouldBe(1);

        database.ActivityRows().ShouldBeEmpty();
        database.Database.Services.GetRequiredService<SystemDbContext>().Set<OutboxMessage>().AsNoTracking().Single().Status.ShouldBe(OutboxStatus.Delivered);
    }

    [Fact]
    public async Task AnEntryWithoutAnOperationIsWrittenForTheSystemAndAnInvalidActionIsRefused()
    {
        using var database = new ServiceDatabase(On());

        using (var scope = database.Database.NewScope())
        {
            var log = scope.ServiceProvider.GetRequiredService<IActivityLog>();
            await log.WriteAsync(new ActivityEntry("cli.ran") { Ip = "10.0.0.7" }, Token);

            await Should.ThrowAsync<ArgumentException>(async () => await log.WriteAsync(new ActivityEntry(""), Token));
            await Should.ThrowAsync<ArgumentException>(async () => await log.WriteAsync(new ActivityEntry(new string('a', 65)), Token));
        }

        var row = database.ActivityRows().ShouldHaveSingleItem();
        row.ActorType.ShouldBe(PrincipalKind.System);
        row.SiteId.ShouldBeNull();
        row.Ip.ShouldBe("10.0.0.7");
    }

    [Fact]
    public async Task ARepeatedDeliveryWritesNoSecondRow()
    {
        using var database = new ServiceDatabase(On());
        var delivery = new EventDelivery(Guid.CreateVersion7(), 1);

        await database.AsAsync(Editor, async services =>
        {
            var recorder = services.GetRequiredService<ActivityRecorder<SettingsChangedEvent>>();
            await recorder.HandleAsync(new SettingsChangedEvent("seo"), delivery, Token);
            await recorder.HandleAsync(new SettingsChangedEvent("seo"), delivery with { Attempt = 2 }, Token);
            return true;
        });

        database.ActivityRows().ShouldHaveSingleItem().Id.ShouldBe(delivery.Key);
    }

    [Fact]
    public async Task TheRowIsRecordedWithTheWriteAndDeliveredByTheDrain()
    {
        using var database = new ServiceDatabase(On());

        await database.CreateAsync(Editor, new() { ["title"] = "T" });

        database.ActivityRows().ShouldBeEmpty();
        var pending = database.Database.Services.GetRequiredService<SystemDbContext>().Set<OutboxMessage>().AsNoTracking().ToList();
        pending.Select(row => row.Handler).ShouldBe(["activity.content.draft_saved"]);

        await database.DrainAsync();

        database.ActivityRows().ShouldHaveSingleItem().Action.ShouldBe("content.draft_saved");
    }

    [Fact]
    public async Task AnEventOfAPluginIsLoggedOnceTheHostDescribesIt()
    {
        using var database = new ServiceDatabase(On(cms => cms.Events.AddActivity<Ping>("ping.sent", ping => new ActivityEntry("ping.sent") { TargetType = "ping", TargetLabel = ping.Note })));

        await PublishAsync(database, Editor, new Ping("hello"), origin: Origin.Admin);
        await database.DrainAsync();

        var row = database.ActivityRows().ShouldHaveSingleItem();
        row.Action.ShouldBe("ping.sent");
        row.TargetLabel.ShouldBe("hello");
        row.Origin.ShouldBe(Origin.Admin);
    }

    [Fact]
    public void AnEventDescribedTwiceIsRefused()
    {
        var exception = Should.Throw<FluentCmsConfigurationException>(() => new ServiceCollection().AddFluentCms(cms => cms.Events
            .AddActivity<Ping>("first", _ => new ActivityEntry("first"))
            .AddActivity<Ping>("second", _ => new ActivityEntry("second"))));

        exception.Code.ShouldBe("FCMS2702");
    }

    [Fact]
    public async Task WithTheLogTurnedOffNoRowIsRecorded()
    {
        using var database = new ServiceDatabase();

        await database.CreateAsync(Editor, new() { ["title"] = "T" });

        database.Rows().ShouldBeEmpty();
        database.ActivityRows().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("retention")]
    [InlineData("site")]
    [InlineData("batch")]
    [InlineData("batch-high")]
    [InlineData("cron")]
    public void OptionsThatCannotWorkAreRefusedAtStartup(string which)
    {
        var exception = Should.Throw<FluentCmsConfigurationException>(() => new ServiceCollection().AddFluentCms(cms =>
        {
            cms.UseSqlite("Data Source=activity-options.db");
            switch (which)
            {
                case "retention":
                    cms.Activity.Retention = TimeSpan.Zero;
                    break;
                case "site":
                    cms.Activity.SiteRetention["b"] = TimeSpan.FromDays(-1);
                    break;
                case "batch-high":
                    cms.Activity.PruneBatchSize = 1001;
                    break;
                case "cron":
                    cms.Activity.PruneSchedule = "every night";
                    break;
                default:
                    cms.Activity.PruneBatchSize = 0;
                    break;
            }
        }));

        exception.Code.ShouldBe("FCMS2701");
    }
}
