using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Events;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Events;

public sealed class EventRecordTests
{
    private static readonly OperationContext Operation = new(
        new Principal(PrincipalKind.AdminUser, Guid.Parse("00000000-0000-0000-0000-000000000001"), "Amir"),
        Origin.Admin,
        new SiteId(Guid.Parse("00000000-0000-0000-0000-0000000000a1")),
        "en",
        null,
        "corr-1",
        new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero))
    {
        Actor = new Principal(PrincipalKind.AdminUser, Guid.Parse("00000000-0000-0000-0000-000000000002"), "Sam"),
    };

    private static readonly Guid Entry = Guid.Parse("00000000-0000-0000-0000-0000000000e1");

    public static TheoryData<CmsEvent> Catalogue() =>
    [
        new EntryDraftSavedEvent("article", Entry, "en", 3),
        new EntryPublishedEvent("article", Entry, "en", 3),
        new EntryUnpublishedEvent("article", Entry, "en", 3),
        new EntryDraftDiscardedEvent("article", Entry, "en", 2),
        new EntryVersionRestoredEvent("article", Entry, "en", 4),
        new EntryTrashedEvent("article", Entry, "en", 3),
        new EntryRestoredEvent("article", Entry, "en", 3),
        new EntryPurgedEvent("article", Entry, "en", 3),
        new EntryScheduledEvent("article", Entry, "en", 3, new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero), "publish"),
        new EntryScheduleCancelledEvent("article", Entry, "en", 3, "publish"),
        new SettingsChangedEvent("general"),
        new SiteCreatedEvent(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "main"),
        new SiteUpdatedEvent(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "main"),
        new CacheInvalidatedEvent(["site:1", "entry:2"]),
        new JobFailedEvent(Guid.Parse("00000000-0000-0000-0000-0000000000b1"), "probe", "default", 1, 3, "It failed."),
        new JobDeadLetteredEvent(Guid.Parse("00000000-0000-0000-0000-0000000000b1"), "probe", "default", 3, "It failed."),
    ];

    [Theory]
    [MemberData(nameof(Catalogue))]
    public void EveryEventOfTheCatalogueSurvivesAJsonRoundTripThatTheOutboxMakes(CmsEvent original)
    {
        var stamped = original switch
        {
            EntryDraftSavedEvent saved => saved with { Source = EventSource.From(Operation) },
            _ => original,
        };

        var json = JsonSerializer.Serialize(stamped, stamped.GetType(), JsonSerializerOptions.Web);
        var read = (CmsEvent)JsonSerializer.Deserialize(json, stamped.GetType(), JsonSerializerOptions.Web)!;

        JsonSerializer.Serialize(read, read.GetType(), JsonSerializerOptions.Web).ShouldBe(json);
        read.GetType().ShouldBe(stamped.GetType());
        read.EventId.ShouldBe(stamped.EventId);
        read.Source.ShouldBe(stamped.Source);
    }

    [Fact]
    public void TheCatalogueHoldsEveryEventOfSpecSection411ThatThisIssueDefines()
    {
        var names = typeof(CmsEvent).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true } && type.IsAssignableTo(typeof(CmsEvent)))
            .Select(type => type.Name)
            .Order()
            .ToList();

        names.ShouldBe(
        [
            "ApiKeyCreatedEvent",
            "ApiKeyExpiryChangedEvent",
            "ApiKeyRevokedEvent",
            "CacheInvalidatedEvent",
            "EntryDraftDiscardedEvent",
            "EntryDraftSavedEvent",
            "EntryPublishedEvent",
            "EntryPurgedEvent",
            "EntryRestoredEvent",
            "EntryScheduleCancelledEvent",
            "EntryScheduledEvent",
            "EntryTrashedEvent",
            "EntryUnpublishedEvent",
            "EntryVersionRestoredEvent",
            "JobDeadLetteredEvent",
            "JobFailedEvent",
            "LoginFailedEvent",
            "LoginSucceededEvent",
            "SettingsChangedEvent",
            "SiteCreatedEvent",
            "SiteUpdatedEvent",
            "UserCreatedEvent",
            "UserDeactivatedEvent",
            "UserLoginLinkedEvent",
            "UserLoginUnlinkedEvent",
            "UserRoleChangedEvent",
            "UserUpdatedEvent",
        ]);
    }

    [Fact]
    public void ASourceNamesTheActorThatAJobImpersonatesNotTheSystemPrincipal()
    {
        var source = EventSource.From(Operation);

        source.ActorId.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        source.ActorName.ShouldBe("Sam");
        source.SiteId.ShouldBe(Guid.Parse("00000000-0000-0000-0000-0000000000a1"));
        source.OccurredAt.ShouldBe(Operation.Timestamp);
        source.Origin.ShouldBe(Origin.Admin);
        source.CorrelationId.ShouldBe("corr-1");
    }

    [Fact]
    public async Task AJobThatFailsRaisesTheEventsOnTheBusAndNeverFailsTheJob()
    {
        var seen = new Seen();
        using var fixture = new JobFixture();
        using var instance = fixture.Database.CreateServices(configure: cms =>
        {
            cms.Services.AddSingleton<TimeProvider>(fixture.Clock);
            cms.Services.AddSingleton(seen);
            cms.Events.AddHandler<JobFailedEvent, FailedHandler>();
            cms.Events.AddHandler<JobDeadLetteredEvent, DeadLetteredHandler>();
        });
        var id = fixture.AddRow("no-such-job", "{}", maxAttempts: 1);

        (await instance.GetRequiredService<Core.Jobs.JobExecutor>().RunNextAsync("default", "worker", TestContext.Current.CancellationToken)).ShouldBeTrue();

        seen.Calls.ShouldBe(["job-failed", "job-dead-lettered"]);
        var failed = seen.Of<JobFailedEvent>().Single();
        failed.JobId.ShouldBe(id);
        failed.Attempt.ShouldBe(1);
        failed.Source.OccurredAt.ShouldBe(JobFixture.Start);
        seen.Of<JobDeadLetteredEvent>().Single().JobId.ShouldBe(id);
    }

    [Fact]
    public async Task AHandlerOfJobEventsThatThrowsLeavesTheJobSettled()
    {
        var seen = new Seen();
        seen.Behavior = (_, _) => throw new InvalidOperationException("The handler is broken.");
        using var fixture = new JobFixture();
        using var instance = fixture.Database.CreateServices(configure: cms =>
        {
            cms.Services.AddSingleton<TimeProvider>(fixture.Clock);
            cms.Services.AddSingleton(seen);
            cms.Events.AddHandler<JobFailedEvent, FailedHandler>();
            cms.Events.AddHandler<JobDeadLetteredEvent, DeadLetteredHandler>();
        });
        var id = fixture.AddRow("no-such-job", "{}", maxAttempts: 1);

        await instance.GetRequiredService<Core.Jobs.JobExecutor>().RunNextAsync("default", "worker", TestContext.Current.CancellationToken);

        fixture.Job(id).Status.ShouldBe(Core.Jobs.JobStatus.DeadLettered);
    }

    private sealed class FailedHandler(Seen seen) : IEventHandler<JobFailedEvent>
    {
        public ValueTask HandleAsync(JobFailedEvent message, CancellationToken cancellationToken) => seen.HeardAsync("job-failed", message, null, cancellationToken);
    }

    private sealed class DeadLetteredHandler(Seen seen) : IEventHandler<JobDeadLetteredEvent>
    {
        public ValueTask HandleAsync(JobDeadLetteredEvent message, CancellationToken cancellationToken) => seen.HeardAsync("job-dead-lettered", message, null, cancellationToken);
    }
}
