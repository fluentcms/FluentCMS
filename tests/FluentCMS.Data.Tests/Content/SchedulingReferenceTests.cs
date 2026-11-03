using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Events;
using FluentCMS.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;
using static FluentCMS.Data.Tests.Content.SchedulingFixture;

namespace FluentCMS.Data.Tests.Content;

// The job publishes as the system principal, which sees every reference target. Under Block that would let a principal
// publish what it could not publish itself, so a schedule is held to what the principal who made it could see.
public sealed class SchedulingReferenceTests : IDisposable
{
    private static readonly PermissionConstraints OwnEntries = new() { RowRule = BuiltInRoles.OwnEntriesRule };

    private readonly SchedulingFixture _fixture = new(schema: SampleSchema.NewBuilder()
        .AddCollection("author", type => type.Localized().Field("name", "text", field => field.Localizable()))
        .AddCollection("article", type => type
            .Localized()
            .PublishPolicy(UnpublishedReferencePolicy.Block)
            .Field("title", "text", field => field.Localizable())
            .Field("author", "relation", field => field.Relation("author")))
        .Build());

    private ServiceDatabase Database => _fixture.Database;

    public void Dispose() => _fixture.Dispose();

    private Principal Publisher(params (string Permission, PermissionConstraints? Constraints)[] more) =>
        WithRole(Database.Database.AddRoleForTest(Guid.NewGuid().ToString("N"), [("content.article.read", null), ("content.article.publish", null), .. more]));

    private async Task<Guid> AuthorAsync(bool published, string name = "Ann")
    {
        var id = IdOf(await Database.CreateAsync(Editor, new() { ["name"] = name }, "author"));
        if (published)
        {
            await Database.PublishAsync(Editor, id, type: "author");
        }

        return id;
    }

    private async Task<Guid> ArticleAsync(Guid? author)
    {
        ContentDocument entry = new() { ["title"] = "One" };
        if (author is { } id)
        {
            entry["author"] = id;
        }

        return IdOf(await Database.CreateAsync(Editor, entry));
    }

    [Fact]
    public async Task APrincipalThatCannotReadTheTypeOfAReferenceCannotScheduleAPublishOfIt()
    {
        var blind = Publisher();
        var article = await ArticleAsync(await AuthorAsync(published: true));

        await Should.ThrowAsync<ValidationException>(() => Database.PublishAsync(blind, article));
        var error = (await Should.ThrowAsync<ValidationException>(() => _fixture.ScheduleAsync(blind, article, At(Start.AddHours(1))))).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(PublishWarningCodes.UnavailableReference);
        error.Path.ShouldBe("");
        _fixture.Schedules(article).ShouldBeEmpty();
        _fixture.Jobs().ShouldBeEmpty();
    }

    [Fact]
    public async Task AReferenceThatTheEntryAcquiresAfterwardsIsHeldToTheSchedulersViewWhenTheJobRuns()
    {
        var blind = Publisher();
        var article = await ArticleAsync(null);
        await _fixture.ScheduleAsync(blind, article, At(Start.AddHours(1)));
        var author = await AuthorAsync(published: true);
        await Database.PatchAsync(Editor, article, new() { ["author"] = author }, 1);

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        Database.Document(article).Status.ShouldBe(EntryStatus.Draft);
        var row = _fixture.Schedules(article).Single();
        row.Status.ShouldBe(ScheduleRow.Failed);
        row.Error.ShouldNotBeNull().ShouldContain(PublishWarningCodes.UnavailableReference);
        _fixture.Seen.Of<EntryPublishedEvent>().Where(item => item.EntryId == article).ShouldBeEmpty();
    }

    [Fact]
    public async Task ATargetNobodyCanReadFromTheSchedulerReadsAsOneThatIsNotThereWhenTheJobRuns()
    {
        var own = Publisher(("content.author.read", OwnEntries));
        var author = await AuthorAsync(published: true);
        var article = await ArticleAsync(author);

        await _fixture.ScheduleAsync(own, article, At(Start.AddHours(1)));
        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        Database.Document(article).Status.ShouldBe(EntryStatus.Draft);
        _fixture.Schedules(article).Single().Error.ShouldNotBeNull().ShouldContain(PublishWarningCodes.UnavailableReference);
    }

    [Fact]
    public async Task ATargetThatIsNotPublishedYetDoesNotStopTheScheduleAndIsPublishedByThenItRuns()
    {
        var reader = Publisher(("content.author.read", null));
        var author = await AuthorAsync(published: false);
        var article = await ArticleAsync(author);
        await Should.ThrowAsync<ValidationException>(() => Database.PublishAsync(reader, article));

        await _fixture.ScheduleAsync(reader, article, At(Start.AddHours(2)));
        await Database.PublishAsync(Editor, author, type: "author");
        _fixture.AdvanceTo(Start.AddHours(2));
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        Database.Document(article).Status.ShouldBe(EntryStatus.Published);
        _fixture.Schedules(article).Single().Status.ShouldBe(ScheduleRow.Executed);
    }

    [Fact]
    public async Task ATargetStillNotPublishedWhenTheJobRunsStopsThePublishAndSaysWhichWay()
    {
        var reader = Publisher(("content.author.read", null));
        var author = await AuthorAsync(published: false);
        var article = await ArticleAsync(author);
        await _fixture.ScheduleAsync(reader, article, At(Start.AddHours(1)));

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        Database.Document(article).Status.ShouldBe(EntryStatus.Draft);
        _fixture.Schedules(article).Single().Error.ShouldNotBeNull().ShouldContain(PublishWarningCodes.UnpublishedReference);
    }

    [Fact]
    public async Task APrincipalThatSeesEveryTargetIsHeldToThePolicyToo()
    {
        var author = await AuthorAsync(published: false);
        var article = await ArticleAsync(author);
        await _fixture.ScheduleAsync(Editor, article, At(Start.AddHours(1)));

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        Database.Document(article).Status.ShouldBe(EntryStatus.Draft);
        _fixture.Schedules(article).Single().Status.ShouldBe(ScheduleRow.Failed);
    }

    [Fact]
    public async Task ASchedulePutInBySystemCodeIsNotHeldToAnyonesView()
    {
        var author = await AuthorAsync(published: true);
        var article = await ArticleAsync(author);
        await Database.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().ScheduleAsync("article", article, At(Start.AddHours(1)), null, Token), origin: Origin.Seed);

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        Database.Document(article).Status.ShouldBe(EntryStatus.Published);
    }
}
