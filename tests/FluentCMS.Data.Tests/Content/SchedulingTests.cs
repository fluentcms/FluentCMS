using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Activity;
using FluentCMS.Events;
using FluentCMS.Hooks;
using FluentCMS.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;
using static FluentCMS.Data.Tests.Content.SchedulingFixture;

namespace FluentCMS.Data.Tests.Content;

public sealed class SchedulingTests : IDisposable
{
    private readonly SchedulingFixture _fixture = new();

    private ServiceDatabase Database => _fixture.Database;

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task AScheduleInANamedZoneRunsAtTheInstantThatZoneGivesThatDay()
    {
        var id = await _fixture.DraftAsync();

        var winter = await _fixture.ScheduleAsync(Editor, id, new ScheduleRequest(ScheduleAction.Publish, new DateTime(2027, 3, 27, 12, 0, 0, DateTimeKind.Unspecified), "Europe/Berlin"));
        winter.RunAt.ShouldBe(new DateTimeOffset(2027, 3, 27, 11, 0, 0, TimeSpan.Zero));
        winter.TimeZone.ShouldBe("Europe/Berlin");

        var summer = await _fixture.ScheduleAsync(Editor, id, new ScheduleRequest(ScheduleAction.Publish, new DateTime(2027, 3, 29, 12, 0, 0, DateTimeKind.Unspecified), "Europe/Berlin"));
        var instant = new DateTimeOffset(2027, 3, 29, 10, 0, 0, TimeSpan.Zero);
        summer.RunAt.ShouldBe(instant);

        var row = _fixture.Schedules(id).ShouldHaveSingleItem();
        row.RunAtUtc.ShouldBe(instant);
        row.TimeZone.ShouldBe("Europe/Berlin");
        _fixture.Jobs().ShouldHaveSingleItem().RunAt.ShouldBe(instant);
        Database.Document(id).SchedulePublishAt.ShouldBe(instant);

        _fixture.AdvanceTo(instant - TimeSpan.FromSeconds(1));
        (await _fixture.RunDueAsync()).ShouldBeFalse();
        Database.Document(id).Status.ShouldBe(EntryStatus.Draft);

        _fixture.AdvanceTo(instant);
        (await _fixture.RunDueAsync()).ShouldBeTrue();
        Database.Document(id).Status.ShouldBe(EntryStatus.Published);
    }

    [Fact]
    public async Task TheJobPublishesAsTheSchedulingUserWithTheOriginOfAJob()
    {
        var user = _fixture.Publisher();
        var id = await _fixture.DraftAsync();
        await _fixture.ScheduleAsync(user, id, At(Start.AddHours(1)));

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        var document = Database.Document(id);
        document.Status.ShouldBe(EntryStatus.Published);
        document.PublishedVersion.ShouldBe(1);
        document.UpdatedBy.ShouldBe(user.Id);
        var published = _fixture.Seen.Of<EntryPublishedEvent>().ShouldHaveSingleItem();
        published.EntryId.ShouldBe(id);
        published.Source.ActorId.ShouldBe(user.Id);
        published.Source.ActorKind.ShouldBe(PrincipalKind.AdminUser);
        published.Source.Origin.ShouldBe(Origin.Job);
        _fixture.Schedules(id).ShouldHaveSingleItem().Status.ShouldBe(ScheduleRow.Executed);
        _fixture.Jobs().ShouldHaveSingleItem().Status.ShouldBe(JobStatus.Succeeded);
        Database.Document(id).SchedulePublishAt.ShouldBeNull();
        Database.Versions(id).ShouldHaveSingleItem().StatusAfter.ShouldBe(EntryStatus.Published);
    }

    [Fact]
    public async Task ASystemPrincipalCanScheduleAndTheJobThenNeedsNoRoles()
    {
        var id = await _fixture.DraftAsync();
        await Database.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().ScheduleAsync("article", id, At(Start.AddHours(1)), null, Token), origin: Origin.Seed);
        _fixture.Schedules(id).ShouldHaveSingleItem().Claims.ShouldBeNull();

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        Database.Document(id).Status.ShouldBe(EntryStatus.Published);
        Database.Document(id).UpdatedBy.ShouldBe(Guid.Empty);
    }

    [Fact]
    public async Task CancellingKeepsTheRowRemovesTheJobAndClearsTheMirrorSoNothingRuns()
    {
        var id = await _fixture.DraftAsync();
        await _fixture.ScheduleAsync(Editor, id, At(Start.AddHours(1)));
        _fixture.Jobs().ShouldHaveSingleItem();

        await _fixture.CancelAsync(Editor, id);

        var row = _fixture.Schedules(id).ShouldHaveSingleItem();
        row.Status.ShouldBe(ScheduleRow.Cancelled);
        row.CompletedAt.ShouldNotBeNull();
        _fixture.Jobs().ShouldBeEmpty();
        Database.Document(id).SchedulePublishAt.ShouldBeNull();
        var cancelled = _fixture.Seen.Of<EntryScheduleCancelledEvent>().ShouldHaveSingleItem();
        cancelled.Action.ShouldBe("publish");
        cancelled.Version.ShouldBe(1);

        _fixture.AdvanceTo(Start.AddHours(2));
        (await _fixture.RunDueAsync()).ShouldBeFalse();
        Database.Document(id).Status.ShouldBe(EntryStatus.Draft);

        var error = await Should.ThrowAsync<ValidationException>(() => _fixture.CancelAsync(Editor, id));
        error.Errors.ShouldHaveSingleItem().Code.ShouldBe(ContentErrorCodes.NotScheduled);
    }

    [Fact]
    public async Task ACancelledJobThatHadAlreadyStartedFindsItsScheduleClosedAndDoesNothing()
    {
        var user = _fixture.Publisher();
        var id = await _fixture.DraftAsync();
        await _fixture.ScheduleAsync(user, id, At(Start.AddHours(1)));
        var row = _fixture.Schedules(id).Single();
        await _fixture.CancelAsync(Editor, id);

        (await _fixture.RunAsync(row.Id, row.JobId!.Value, user)).ShouldBe(ScheduleOutcome.Superseded);

        Database.Document(id).Status.ShouldBe(EntryStatus.Draft);
        _fixture.Seen.Of<EntryPublishedEvent>().ShouldBeEmpty();
    }

    [Fact]
    public async Task ReschedulingMovesTheOneRowAndTheOneJobTogether()
    {
        var id = await _fixture.DraftAsync();
        await _fixture.ScheduleAsync(Editor, id, At(Start.AddHours(1)));
        var first = _fixture.Schedules(id).Single();
        var firstJob = _fixture.Jobs().Single();

        var moved = await _fixture.ScheduleAsync(Editor, id, At(Start.AddHours(3)));

        var row = _fixture.Schedules(id).ShouldHaveSingleItem();
        row.Id.ShouldBe(first.Id);
        row.RunAtUtc.ShouldBe(moved.RunAt);
        row.JobId.ShouldNotBe(firstJob.Id);
        var job = _fixture.Jobs().ShouldHaveSingleItem();
        job.Id.ShouldBe(row.JobId!.Value);
        job.RunAt.ShouldBe(moved.RunAt);
        Database.Document(id).SchedulePublishAt.ShouldBe(moved.RunAt);
        _fixture.Seen.Of<EntryScheduledEvent>().Select(item => item.At).ShouldBe([Start.AddHours(1), Start.AddHours(3)]);

        _fixture.AdvanceTo(Start.AddHours(2));
        (await _fixture.RunDueAsync()).ShouldBeFalse();
        Database.Document(id).Status.ShouldBe(EntryStatus.Draft);

        _fixture.AdvanceTo(Start.AddHours(3));
        (await _fixture.RunDueAsync()).ShouldBeTrue();
        Database.Document(id).Status.ShouldBe(EntryStatus.Published);
        _fixture.Seen.Of<EntryPublishedEvent>().Count().ShouldBe(1);
        _fixture.Schedules(id).ShouldHaveSingleItem().Status.ShouldBe(ScheduleRow.Executed);
    }

    [Fact]
    public async Task ReschedulingToAnEarlierTimeRunsThenAndNotAgainAtTheOldTime()
    {
        var id = await _fixture.DraftAsync();
        await _fixture.ScheduleAsync(Editor, id, At(Start.AddHours(5)));
        await _fixture.ScheduleAsync(Editor, id, At(Start.AddHours(1)));

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();
        _fixture.AdvanceTo(Start.AddHours(6));
        (await _fixture.RunDueAsync()).ShouldBeFalse();

        _fixture.Seen.Of<EntryPublishedEvent>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task AJobThatALaterRescheduleLeftBehindDoesNothing()
    {
        var user = _fixture.Publisher();
        var id = await _fixture.DraftAsync();
        await _fixture.ScheduleAsync(user, id, At(Start.AddHours(1)));
        var stale = _fixture.Schedules(id).Single().JobId!.Value;

        // The job had been claimed when the reschedule came, so it could not be removed with the row's old time.
        Database.Read(context => context.Set<Job>().Where(job => job.Id == stale).ExecuteUpdate(set => set.SetProperty(job => job.Status, JobStatus.Running)));
        await _fixture.ScheduleAsync(user, id, At(Start.AddHours(2)));
        var row = _fixture.Schedules(id).Single();
        row.JobId.ShouldNotBe(stale);
        _fixture.Jobs().Count.ShouldBe(2);

        (await _fixture.RunAsync(row.Id, stale, user)).ShouldBe(ScheduleOutcome.Superseded);

        Database.Document(id).Status.ShouldBe(EntryStatus.Draft);
        _fixture.Schedules(id).Single().Status.ShouldBe(ScheduleRow.Pending);
    }

    [Fact]
    public async Task TwoWorkersRunningTheSameJobPublishOnce()
    {
        var user = _fixture.Publisher();
        var id = await _fixture.DraftAsync();
        await _fixture.ScheduleAsync(user, id, At(Start.AddHours(1)));
        var row = _fixture.Schedules(id).Single();
        _fixture.AdvanceTo(Start.AddHours(1));

        var outcomes = await Task.WhenAll(_fixture.RunAsync(row.Id, row.JobId!.Value, user), _fixture.RunAsync(row.Id, row.JobId!.Value, user));

        outcomes.Count(outcome => outcome == ScheduleOutcome.Executed).ShouldBe(1);
        outcomes.Count(outcome => outcome == ScheduleOutcome.Superseded).ShouldBe(1);
        _fixture.Seen.Of<EntryPublishedEvent>().Count().ShouldBe(1);
        (await _fixture.RunAsync(row.Id, row.JobId!.Value, user)).ShouldBe(ScheduleOutcome.Superseded);
    }

    [Fact]
    public async Task AScheduledUnpublishWithdrawsTheLocaleAndTheMirrorsAreKeptPerAction()
    {
        var id = await _fixture.PublishedAsync();
        await _fixture.ScheduleAsync(Editor, id, At(Start.AddHours(2), ScheduleAction.Unpublish));
        await _fixture.ScheduleAsync(Editor, id, At(Start.AddHours(1)));

        var document = Database.Document(id);
        document.SchedulePublishAt.ShouldBe(Start.AddHours(1));
        document.ScheduleUnpublishAt.ShouldBe(Start.AddHours(2));
        _fixture.Schedules(id).Count.ShouldBe(2);

        _fixture.AdvanceTo(Start.AddHours(2));
        (await _fixture.RunDueAsync()).ShouldBeTrue();
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        document = Database.Document(id);
        document.Status.ShouldBe(EntryStatus.Unpublished);
        document.SchedulePublishAt.ShouldBeNull();
        document.ScheduleUnpublishAt.ShouldBeNull();
        _fixture.Seen.Of<EntryUnpublishedEvent>().ShouldHaveSingleItem().Source.Origin.ShouldBe(Origin.Job);
    }

    [Fact]
    public async Task EachLocaleHasItsOwnScheduleAndCancellingOneLeavesTheOther()
    {
        var id = await _fixture.DraftAsync();
        await Database.UpdateAsync(Editor, id, new() { ["title"] = "Un" }, 0, locale: "fr");
        await _fixture.ScheduleAsync(Editor, id, At(Start.AddHours(1)), "en");
        await _fixture.ScheduleAsync(Editor, id, At(Start.AddHours(2)), "fr");
        Database.Document(id, "en").SchedulePublishAt.ShouldBe(Start.AddHours(1));
        Database.Document(id, "fr").SchedulePublishAt.ShouldBe(Start.AddHours(2));

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();
        Database.Document(id, "en").Status.ShouldBe(EntryStatus.Published);
        Database.Document(id, "fr").Status.ShouldBe(EntryStatus.Draft);

        await _fixture.CancelAsync(Editor, id, locale: "fr");
        _fixture.AdvanceTo(Start.AddHours(3));
        (await _fixture.RunDueAsync()).ShouldBeFalse();
        Database.Document(id, "fr").Status.ShouldBe(EntryStatus.Draft);
        Database.Document(id, "fr").SchedulePublishAt.ShouldBeNull();
    }

    [Fact]
    public async Task APublishThatHasNothingToPublishAndAnUnpublishOfAnUnpublishedEntryAreSkipped()
    {
        var published = await _fixture.PublishedAsync();
        var draft = await _fixture.DraftAsync("Draft");
        await _fixture.ScheduleAsync(Editor, published, At(Start.AddHours(1)));
        await _fixture.ScheduleAsync(Editor, draft, At(Start.AddHours(1), ScheduleAction.Unpublish));
        var before = _fixture.Seen.Of<EntryPublishedEvent>().Count();

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        _fixture.Schedules(published).Single().Status.ShouldBe(ScheduleRow.Skipped);
        _fixture.Schedules(draft).Single().Status.ShouldBe(ScheduleRow.Skipped);
        _fixture.Seen.Of<EntryPublishedEvent>().Count().ShouldBe(before);
        Database.Document(published).SchedulePublishAt.ShouldBeNull();
        Database.Document(draft).ScheduleUnpublishAt.ShouldBeNull();
    }

    [Fact]
    public async Task AScheduleOfAnEntryInTheTrashIsSkippedAndOneRestoredBeforeItIsDueStillRuns()
    {
        var trashed = await _fixture.DraftAsync("Trashed");
        var restored = await _fixture.DraftAsync("Restored");
        await _fixture.ScheduleAsync(Editor, trashed, At(Start.AddHours(1)));
        await _fixture.ScheduleAsync(Editor, restored, At(Start.AddHours(1)));
        await Database.DeleteAsync(Editor, trashed);
        await Database.DeleteAsync(Editor, restored);
        await Database.RestoreAsync(Editor, restored);

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        var row = _fixture.Schedules(trashed).Single();
        row.Status.ShouldBe(ScheduleRow.Skipped);
        row.Error.ShouldNotBeNull();
        Database.Document(trashed).Status.ShouldBe(EntryStatus.Draft);
        Database.Document(trashed).SchedulePublishAt.ShouldBeNull();
        Database.Document(restored).Status.ShouldBe(EntryStatus.Published);

        await Database.RestoreAsync(Editor, trashed);
        Database.Document(trashed).Status.ShouldBe(EntryStatus.Draft);
    }

    [Fact]
    public async Task PurgingAnEntryRemovesItsSchedulesAndTheirJobs()
    {
        var id = await _fixture.DraftAsync();
        await _fixture.ScheduleAsync(Editor, id, At(Start.AddHours(1)));
        await _fixture.ScheduleAsync(Editor, id, At(Start.AddHours(2), ScheduleAction.Unpublish));
        var other = await _fixture.DraftAsync("Other");
        await _fixture.ScheduleAsync(Editor, other, At(Start.AddHours(1)));
        _fixture.Jobs().Count.ShouldBe(3);
        await Database.DeleteAsync(Editor, id);

        await Database.PurgeAsync(WithRole(BuiltInRoles.SuperAdmin), id);

        _fixture.Schedules(id).ShouldBeEmpty();
        _fixture.Jobs().ShouldHaveSingleItem().Id.ShouldBe(_fixture.Schedules(other).Single().JobId!.Value);
        _fixture.AdvanceTo(Start.AddHours(3));
        (await _fixture.RunDueAsync()).ShouldBeTrue();
        (await _fixture.RunDueAsync()).ShouldBeFalse();
        Database.Document(other).Status.ShouldBe(EntryStatus.Published);
    }

    [Fact]
    public async Task AScheduleNeedsTheSamePermissionAsDoingItNow()
    {
        var id = await _fixture.DraftAsync();
        var reader = WithRole(_fixture.RoleWith("content.article.read"));
        var publishOnly = WithRole(_fixture.RoleWith("content.article.read", "content.article.publish"));
        var unpublishOnly = WithRole(_fixture.RoleWith("content.article.read", "content.article.unpublish"));

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _fixture.ScheduleAsync(reader, id, At(Start.AddHours(1))));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _fixture.ScheduleAsync(reader, id, At(Start.AddHours(1), ScheduleAction.Unpublish)));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _fixture.ScheduleAsync(unpublishOnly, id, At(Start.AddHours(1))));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _fixture.ScheduleAsync(publishOnly, id, At(Start.AddHours(1), ScheduleAction.Unpublish)));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _fixture.ScheduleAsync(Principal.Anonymous, id, At(Start.AddHours(1))));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _fixture.CancelAsync(reader, id));
        _fixture.Schedules(id).ShouldBeEmpty();
        _fixture.Jobs().ShouldBeEmpty();
        _fixture.Seen.Of<EntryScheduledEvent>().ShouldBeEmpty();

        (await _fixture.ScheduleAsync(publishOnly, id, At(Start.AddHours(1)))).Action.ShouldBe(ScheduleAction.Publish);
        (await _fixture.ScheduleAsync(unpublishOnly, id, At(Start.AddHours(2), ScheduleAction.Unpublish))).Action.ShouldBe(ScheduleAction.Unpublish);
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _fixture.CancelAsync(unpublishOnly, id));
        await _fixture.CancelAsync(publishOnly, id);
    }

    [Fact]
    public async Task AScheduleHoldsTheLocaleAndSiteLimitsOfTheGrant()
    {
        var id = await _fixture.DraftAsync();
        await Database.UpdateAsync(Editor, id, new() { ["title"] = "Un" }, 0, locale: "fr");
        var frenchOnly = WithRole(Database.Database.AddRoleForTest(
            "French",
            ("content.article.read", null),
            ("content.article.publish", new PermissionConstraints { Locales = ["fr"] })));
        var siteB = WithRole(Database.Database.AddRoleForTest(
            "Site B",
            ("content.article.read", null),
            ("content.article.publish", new PermissionConstraints { Sites = [SiteB] })));

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _fixture.ScheduleAsync(frenchOnly, id, At(Start.AddHours(1)), "en"));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _fixture.ScheduleAsync(siteB, id, At(Start.AddHours(1)), "en"));
        _fixture.Schedules(id).ShouldBeEmpty();

        (await _fixture.ScheduleAsync(frenchOnly, id, At(Start.AddHours(1)), "fr")).Locale.ShouldBe("fr");
    }

    private static readonly PermissionConstraints OwnEntries = new() { RowRule = BuiltInRoles.OwnEntriesRule };

    private Principal OwnPublisher() => WithRole(Database.Database.AddRoleForTest(
        Guid.NewGuid().ToString("N"),
        ("content.article.create", null),
        ("content.article.read", OwnEntries),
        ("content.article.update", OwnEntries),
        ("content.article.publish", OwnEntries),
        ("content.article.unpublish", OwnEntries)));

    [Fact]
    public async Task AScheduleHoldsTheRowRuleOfTheGrant()
    {
        var own = OwnPublisher();
        var mine = IdOf(await Database.CreateAsync(own, new() { ["title"] = "Mine" }));
        var theirs = await _fixture.DraftAsync("Theirs");

        (await _fixture.ScheduleAsync(own, mine, At(Start.AddHours(1)))).Version.ShouldBe(1);

        await Should.ThrowAsync<EntryNotFoundException>(() => _fixture.ScheduleAsync(own, theirs, At(Start.AddHours(1))));
        _fixture.Schedules(theirs).ShouldBeEmpty();
    }

    [Fact]
    public async Task AGrantLimitedToSomeFieldsCannotScheduleWhatItWouldNotPublishNow()
    {
        var titleOnly = WithRole(Database.Database.AddRoleForTest(
            "Title only",
            ("content.article.read", null),
            ("content.article.publish", new PermissionConstraints { Fields = ["title"] }),
            ("content.article.unpublish", new PermissionConstraints { Fields = ["title"] })));
        var same = await _fixture.PublishedAsync();
        var changed = await _fixture.PublishedAsync("Two");
        await Database.PatchAsync(Editor, changed, new() { ["views"] = 7 }, 1);

        await _fixture.ScheduleAsync(titleOnly, same, At(Start.AddHours(1)));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _fixture.ScheduleAsync(titleOnly, changed, At(Start.AddHours(1))));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _fixture.ScheduleAsync(titleOnly, same, At(Start.AddHours(1), ScheduleAction.Unpublish)));

        _fixture.Schedules(changed).ShouldBeEmpty();
        _fixture.Schedules(same).ShouldHaveSingleItem().Action.ShouldBe(ScheduleRow.Publish);
    }

    [Fact]
    public async Task ARequestThatIsWrongFailsTheSameForAnyIdSoItTellsNothingOfTheEntry()
    {
        var theirs = await _fixture.DraftAsync("Theirs");
        var own = OwnPublisher();
        var past = At(Start.AddHours(-1));
        var badZone = new ScheduleRequest(ScheduleAction.Publish, new DateTime(2027, 5, 1, 8, 0, 0, DateTimeKind.Unspecified), "Nowhere/Atlantis");

        foreach (var request in new[] { past, badZone })
        {
            var errors = new List<string>();
            foreach (var id in new[] { theirs, Guid.CreateVersion7() })
            {
                var error = (await Should.ThrowAsync<ValidationException>(() => _fixture.ScheduleAsync(own, id, request))).Errors.ShouldHaveSingleItem();
                errors.Add($"{error.Path}|{error.Code}|{error.Message}");
            }

            errors.Distinct().ShouldHaveSingleItem();
        }

        _fixture.Schedules(theirs).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEntryThePrincipalCannotReachAnswersLikeOneThatIsNotThereForBothCallsAndCancelTellsNothingOfItsSchedule()
    {
        var own = OwnPublisher();
        var scheduled = await _fixture.DraftAsync("Theirs");
        var unscheduled = await _fixture.DraftAsync("Unscheduled");
        await _fixture.ScheduleAsync(Editor, scheduled, At(Start.AddHours(1)));
        var before = _fixture.Schedules(scheduled).Single();

        foreach (var id in new[] { scheduled, unscheduled, Guid.CreateVersion7() })
        {
            (await Should.ThrowAsync<EntryNotFoundException>(() => _fixture.ScheduleAsync(own, id, At(Start.AddHours(2))))).Type.ShouldBe("article");
            (await Should.ThrowAsync<EntryNotFoundException>(() => _fixture.CancelAsync(own, id))).Type.ShouldBe("article");
        }

        var after = _fixture.Schedules(scheduled).ShouldHaveSingleItem();
        after.Status.ShouldBe(ScheduleRow.Pending);
        after.RunAtUtc.ShouldBe(before.RunAtUtc);
        after.JobId.ShouldBe(before.JobId);
        _fixture.Schedules(unscheduled).ShouldBeEmpty();
    }

    [Fact]
    public async Task ARolePrincipalThatLostThePermissionBeforeTheJobRanPublishesNothing()
    {
        var role = _fixture.RoleWith("content.article.read", "content.article.publish");
        var user = WithRole(role);
        var id = await _fixture.DraftAsync();
        await _fixture.ScheduleAsync(user, id, At(Start.AddHours(1)));
        _fixture.RemoveGrant(role, "content.article.publish");

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        var row = _fixture.Schedules(id).Single();
        row.Status.ShouldBe(ScheduleRow.Failed);
        row.Error.ShouldNotBeNull().ShouldContain("publish");
        row.CompletedAt.ShouldNotBeNull();
        Database.Document(id).Status.ShouldBe(EntryStatus.Draft);
        Database.Document(id).SchedulePublishAt.ShouldBeNull();
        _fixture.Seen.Of<EntryPublishedEvent>().ShouldBeEmpty();
        _fixture.Jobs().ShouldHaveSingleItem().Status.ShouldBe(JobStatus.Succeeded);
    }

    [Fact]
    public async Task ADeletedRoleAndAGrantNarrowedToAnotherLocaleAreNotUsedEither()
    {
        var gone = _fixture.RoleWith("content.article.read", "content.article.publish", "content.article.unpublish");
        var narrowed = Database.Database.AddRoleForTest("Narrowed", ("content.article.read", null), ("content.article.publish", null));
        var first = await _fixture.PublishedAsync("First");
        var second = await _fixture.DraftAsync("Second");
        await _fixture.ScheduleAsync(WithRole(gone), first, At(Start.AddHours(1), ScheduleAction.Unpublish));
        await _fixture.ScheduleAsync(WithRole(narrowed), second, At(Start.AddHours(1)));
        _fixture.RemoveRole(gone);
        _fixture.Change(context =>
        {
            context.Remove(context.Set<RoleGrant>().Single(grant => grant.RoleId == narrowed && grant.Permission == "content.article.publish"));
            context.Add(new RoleGrant { RoleId = narrowed, Permission = "content.article.publish", Constraints = new PermissionConstraints { Locales = ["fr"] }.ToJson() });
        });

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        Database.Document(first).Status.ShouldBe(EntryStatus.Published);
        Database.Document(second).Status.ShouldBe(EntryStatus.Draft);
        _fixture.Schedules(first).Single().Status.ShouldBe(ScheduleRow.Failed);
        _fixture.Schedules(second).Single().Status.ShouldBe(ScheduleRow.Failed);
    }

    [Fact]
    public async Task AnEntryThatLeftTheRowRuleOfTheSchedulerIsNotPublishedByItsJob()
    {
        var own = OwnPublisher();
        var id = IdOf(await Database.CreateAsync(own, new() { ["title"] = "Mine" }));
        await _fixture.ScheduleAsync(own, id, At(Start.AddHours(1)));
        _fixture.Change(context => context.Documents.Where(document => document.DocumentId == id).ExecuteUpdate(set => set.SetProperty(document => document.CreatedBy, Guid.CreateVersion7())));

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        Database.Document(id).Status.ShouldBe(EntryStatus.Draft);
        _fixture.Schedules(id).Single().Status.ShouldBe(ScheduleRow.Failed);
    }

    [Fact]
    public async Task ASchedulingPrincipalWithoutAnActorInTheJobIsRefusedNotTrusted()
    {
        var user = _fixture.Publisher();
        var id = await _fixture.DraftAsync();
        await _fixture.ScheduleAsync(user, id, At(Start.AddHours(1)));
        var row = _fixture.Schedules(id).Single();

        (await _fixture.RunAsync(row.Id, row.JobId!.Value, null)).ShouldBe(ScheduleOutcome.Failed);

        Database.Document(id).Status.ShouldBe(EntryStatus.Draft);
        _fixture.Schedules(id).Single().Status.ShouldBe(ScheduleRow.Failed);
    }

    private sealed class Explodes : IBeforePublishHook<ContentDocument>
    {
        public bool On { get; set; } = true;

        public ValueTask BeforePublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) =>
            On ? throw new InvalidOperationException("The mail server is down.") : ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AFailureThatMayPassIsRetriedAndRecordedOnlyWhenTheLastAttemptIsUsed()
    {
        var explodes = new Explodes();
        using var fixture = new SchedulingFixture(more: cms =>
        {
            cms.Services.AddSingleton(explodes);
            cms.Services.AddSingleton<IBeforePublishHook<ContentDocument>>(services => services.GetRequiredService<Explodes>());
        });
        var user = fixture.Publisher();
        var id = await fixture.DraftAsync();
        await fixture.ScheduleAsync(user, id, At(Start.AddHours(1)));
        var row = fixture.Schedules(id).Single();

        await Should.ThrowAsync<InvalidOperationException>(() => fixture.RunAsync(row.Id, row.JobId!.Value, user));
        fixture.Schedules(id).Single().Status.ShouldBe(ScheduleRow.Pending);
        fixture.Database.Document(id).Status.ShouldBe(EntryStatus.Draft);

        await Should.ThrowAsync<InvalidOperationException>(() => fixture.RunAsync(row.Id, row.JobId!.Value, user, lastAttempt: true));
        var failed = fixture.Schedules(id).Single();
        failed.Status.ShouldBe(ScheduleRow.Failed);
        failed.Error.ShouldBe(nameof(InvalidOperationException));
        failed.Error!.ShouldNotContain("mail");
        fixture.Database.Document(id).SchedulePublishAt.ShouldBeNull();
    }

    [Fact]
    public async Task AFailureThatPassesLetsTheNextAttemptPublish()
    {
        var explodes = new Explodes();
        using var fixture = new SchedulingFixture(more: cms =>
        {
            cms.Services.AddSingleton(explodes);
            cms.Services.AddSingleton<IBeforePublishHook<ContentDocument>>(services => services.GetRequiredService<Explodes>());
        });
        var user = fixture.Publisher();
        var id = await fixture.DraftAsync();
        await fixture.ScheduleAsync(user, id, At(Start.AddHours(1)));
        var row = fixture.Schedules(id).Single();
        await Should.ThrowAsync<InvalidOperationException>(() => fixture.RunAsync(row.Id, row.JobId!.Value, user));

        explodes.On = false;

        (await fixture.RunAsync(row.Id, row.JobId!.Value, user)).ShouldBe(ScheduleOutcome.Executed);
        fixture.Database.Document(id).Status.ShouldBe(EntryStatus.Published);
        fixture.Schedules(id).Single().Status.ShouldBe(ScheduleRow.Executed);
    }

    [Fact]
    public async Task ARefusedScheduleLeavesNoRowJobEventOrMirrorBehind()
    {
        var id = await _fixture.DraftAsync();
        var before = Database.Trace(id);

        await Should.ThrowAsync<ValidationException>(() => _fixture.ScheduleAsync(Editor, id, At(Start.AddHours(-1))));
        await Should.ThrowAsync<ValidationException>(() => _fixture.ScheduleAsync(Editor, id, new ScheduleRequest(ScheduleAction.Publish, new DateTime(2027, 5, 1), "Nowhere/Atlantis")));
        await Should.ThrowAsync<ValidationException>(() => _fixture.ScheduleAsync(Editor, id, new ScheduleRequest((ScheduleAction)7, Start.UtcDateTime.AddDays(1))));

        _fixture.Schedules(id).ShouldBeEmpty();
        _fixture.Jobs().ShouldBeEmpty();
        Database.Document(id).SchedulePublishAt.ShouldBeNull();
        Database.Trace(id).ShouldBe(before);
        _fixture.Seen.Of<EntryScheduledEvent>().ShouldBeEmpty();
    }

    [Fact]
    public async Task ASchedulePutOffByARollbackLeavesTheOldOneAsItWas()
    {
        var id = await _fixture.DraftAsync();
        await _fixture.ScheduleAsync(Editor, id, At(Start.AddHours(1)));
        var before = _fixture.Schedules(id).Single();
        var job = _fixture.Jobs().Single();

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _fixture.ScheduleAsync(WithRole(_fixture.RoleWith("content.article.read")), id, At(Start.AddHours(5))));

        _fixture.Schedules(id).Single().RunAtUtc.ShouldBe(before.RunAtUtc);
        _fixture.Jobs().Single().Id.ShouldBe(job.Id);
        Database.Document(id).SchedulePublishAt.ShouldBe(Start.AddHours(1));
    }

    [Fact]
    public async Task ATypeThatIsNotLocalizedIsScheduledOnItsOneRow()
    {
        var id = IdOf(await Database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));

        var info = await _fixture.ScheduleAsync(Editor, id, At(Start.AddHours(1)), type: "author");
        await _fixture.ScheduleAsync(Editor, id, At(Start.AddHours(2)), "fr", "author");

        info.Locale.ShouldBe("");
        var row = _fixture.Schedules(id).ShouldHaveSingleItem();
        row.Locale.ShouldBe("");
        row.RunAtUtc.ShouldBe(Start.AddHours(2));
        _fixture.AdvanceTo(Start.AddHours(2));
        (await _fixture.RunDueAsync()).ShouldBeTrue();
        Database.Document(id, "").Status.ShouldBe(EntryStatus.Published);
        Database.Document(id, "").SchedulePublishAt.ShouldBeNull();
    }

    public sealed class Logged : IDisposable
    {
        private readonly SchedulingFixture _fixture = new(activity: true);

        public void Dispose() => _fixture.Dispose();

        [Fact]
        public async Task SchedulingReschedulingAndCancellingEachLeaveAnActivityRowWithoutAnyValue()
        {
            var user = _fixture.Publisher();
            var id = await _fixture.DraftAsync("Secret title");
            await _fixture.ScheduleAsync(user, id, At(Start.AddHours(1)));
            await _fixture.ScheduleAsync(user, id, At(Start.AddHours(2)));
            await _fixture.CancelAsync(user, id);
            await _fixture.Database.DrainAsync();

            var rows = _fixture.Database.ActivityRows().Where(row => row.Action.StartsWith("content.schedul", StringComparison.Ordinal)).OrderBy(row => row.OccurredAt).ThenBy(row => row.Action).ToList();

            rows.Select(row => row.Action).Order(StringComparer.Ordinal).ShouldBe(["content.schedule_cancelled", "content.scheduled", "content.scheduled"]);
            rows.ShouldAllBe(row => row.ActorId == user.Id && row.TargetType == "article" && row.TargetId == id.ToString("D") && row.Locale == "en");
            rows.Where(row => row.Action == "content.scheduled").Select(row => row.Details).ShouldAllBe(details => details!.Contains("\"schedule\":\"publish\""));
            rows.Single(row => row.Action == "content.schedule_cancelled").Details!.ShouldContain("\"schedule\":\"publish\"");
            rows.ShouldAllBe(row => row.Details == null || !row.Details.Contains("Secret"));
            _fixture.Seen.Of<EntryScheduledEvent>().Select(item => item.At).ShouldBe([Start.AddHours(1), Start.AddHours(2)]);
        }

        [Fact]
        public async Task ARunLeavesThePublishedRowNamingTheUserAndTheJobOrigin()
        {
            var user = _fixture.Publisher();
            var id = await _fixture.DraftAsync();
            await _fixture.ScheduleAsync(user, id, At(Start.AddHours(1)));
            _fixture.AdvanceTo(Start.AddHours(1));
            (await _fixture.RunDueAsync()).ShouldBeTrue();
            await _fixture.Database.DrainAsync();

            var row = _fixture.Database.ActivityRows().Single(item => item.Action == "content.published");

            row.ActorId.ShouldBe(user.Id);
            row.Origin.ShouldBe(Origin.Job);
        }
    }
}
