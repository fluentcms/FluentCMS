using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// #3534: adding a locale moves the version of every other locale and of a pending publish of each. A schedule saved, or
// run, while a locale is added takes the version the add left, and publishes at its time instead of being skipped as changed.
public sealed class ScheduleRaceTests
{
    private static readonly DateTimeOffset Due = SchedulingFixture.Start.AddHours(1);

    private static Task<ContentDocument> AddFrenchAsync(SchedulingFixture fixture, Guid id) =>
        fixture.Database.UpdateAsync(Editor, id, new() { ["title"] = "Un" }, 0, locale: "fr");

    private static async Task ShouldPublishAtItsTimeAsync(SchedulingFixture fixture, Guid id)
    {
        fixture.Schedules(id).Single().Version.ShouldBe(2);
        fixture.AdvanceTo(Due);
        (await fixture.RunDueAsync()).ShouldBeTrue();

        var row = fixture.Schedules(id).Single();
        (row.Status, row.Error).ShouldBe((ScheduleRow.Executed, null));
        var document = fixture.Database.Document(id);
        (document.Status, document.PublishedVersion).ShouldBe((EntryStatus.Published, 2));
    }

    // The add comes while the schedule is saved: before #3534 it committed then and the schedule kept the version it had read.
    // Now the schedule holds the row first, so the add waits for its commit and moves it. On SQLite, which has one writer,
    // a schedule that has written nothing yet lets the add commit, and one that holds its row makes it wait.
    [Fact]
    public async Task AScheduleSavedWhileALocaleIsAddedPublishesAtItsTime()
    {
        var gate = new CommandGate(command => TestProvider.IsInsertInto(command.CommandText, "cms_job"), after: false);
        using var fixture = new SchedulingFixture(more: cms => cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(gate)));
        var user = fixture.Publisher("content.article.update");
        var id = await fixture.DraftAsync();
        Task? adding = null;

        await fixture.Database.AsAsync(user, async services =>
        {
            var context = services.GetRequiredService<CmsDbContext>();
            var baseline = await RowLocks.ChangesAsync(context);
            gate.Context = context;
            gate.At = async () => adding = await RowLocks.WhileHeldAsync(context, baseline, fixture.Database.Database.ConnectionString, () => AddFrenchAsync(fixture, id), Token);
            return await services.GetRequiredService<IContentService>().ScheduleAsync("article", id, SchedulingFixture.At(Due), null, Token);
        }).WaitAsync(RowLocks.Limit, Token);

        gate.Passed.ShouldBeTrue();
        await adding!.WaitAsync(RowLocks.Limit, Token);
        await ShouldPublishAtItsTimeAsync(fixture, id);
    }

    // The add commits between the job's read of its schedule and its transaction, which before #3534 compared that read.
    [Fact]
    public async Task AScheduleThatRunsWhileALocaleIsAddedPublishes()
    {
        var gate = new TransactionGate();
        using var fixture = new SchedulingFixture(more: cms => cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(gate)));
        var user = fixture.Publisher("content.article.update");
        var id = await fixture.DraftAsync();
        await fixture.ScheduleAsync(user, id, SchedulingFixture.At(Due));
        var row = fixture.Schedules(id).Single();
        gate.At = () => AddFrenchAsync(fixture, id);

        var outcome = await fixture.RunAsync(row.Id, row.JobId!.Value, user, prepare: services => gate.Context = services.GetRequiredService<CmsDbContext>()).WaitAsync(RowLocks.Limit, Token);

        gate.Passed.ShouldBeTrue();
        outcome.ShouldBe(ScheduleOutcome.Executed);
        var ran = fixture.Schedules(id).Single();
        (ran.Status, ran.Version).ShouldBe((ScheduleRow.Executed, 2));
        (fixture.Database.Document(id).PublishedVersion, fixture.Database.Document(id, "fr").PublishedVersion).ShouldBe((2, null));
    }
}
