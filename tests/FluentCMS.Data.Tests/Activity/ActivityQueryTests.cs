using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Activity.ActivityFixture;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Activity;

// What the Management endpoint reads: the log of the site of the request, newest first, narrowed by who, from where, what,
// to what and when.
public sealed class ActivityQueryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Ann = Guid.Parse("00000000-0000-0000-0000-00000000a001");

    private static readonly Guid Bob = Guid.Parse("00000000-0000-0000-0000-00000000b001");

    private static readonly Guid First = Guid.Parse("00000000-0000-0000-0000-00000000e001");

    private static readonly Guid Second = Guid.Parse("00000000-0000-0000-0000-00000000e002");

    private static ServiceDatabase Seeded()
    {
        var database = new ServiceDatabase();
        database.Add(
            Row("content.draft_saved", SiteA.Value, Noon, Ann, Origin.Admin, "article", First.ToString("D")),
            Row("content.published", SiteA.Value, Noon.AddMinutes(1), Ann, Origin.Admin, "article", First.ToString("D")),
            Row("content.published", SiteA.Value, Noon.AddMinutes(2), Bob, Origin.Mcp, "article", Second.ToString("D")),
            Row("site.updated", SiteA.Value, Noon.AddHours(1), Bob, Origin.Api, "site", SiteA.Value.ToString("D")),
            Row("settings.changed", SiteA.Value, Noon.AddDays(1), Ann, Origin.Api, "settings", "seo"),
            Row("content.published", SiteB.Value, Noon, Ann, Origin.Admin, "article", First.ToString("D")),
            Row("job.failed", null, Noon, Guid.Empty, Origin.Job, "job", "7"));
        return database;
    }

    private static Task<PagedResult<ActivityItem>> QueryAsync(ServiceDatabase database, ActivityFilter? filter = null, PageRequest? page = null, SiteId? site = null) =>
        database.AsAsync(Editor, services => services.GetRequiredService<IActivityQuery>().QueryAsync(filter, page, Token), site: site ?? SiteA);

    private static async Task<List<string>> ActionsAsync(ServiceDatabase database, ActivityFilter filter) =>
        [.. (await QueryAsync(database, filter)).Items.Select(item => item.Action)];

    [Fact]
    public async Task ItReadsTheRowsOfTheSiteOfTheOperationNewestFirst()
    {
        using var database = Seeded();

        var page = await QueryAsync(database);

        page.Items.Select(item => item.Action).ShouldBe(["settings.changed", "site.updated", "content.published", "content.published", "content.draft_saved"]);
        page.Total.ShouldBe(5);
        page.Items.ShouldAllBe(item => item.SiteId == SiteA.Value);
        (await QueryAsync(database, site: SiteB)).Items.ShouldHaveSingleItem().Action.ShouldBe("content.published");
    }

    [Fact]
    public async Task ItFiltersByActor()
    {
        using var database = Seeded();

        (await QueryAsync(database, new ActivityFilter { ActorId = Bob })).Items.Select(item => item.Action).ShouldBe(["site.updated", "content.published"]);
        (await QueryAsync(database, new ActivityFilter { ActorId = Ann, ActorKind = PrincipalKind.Member })).Items.ShouldBeEmpty();
        (await QueryAsync(database, new ActivityFilter { ActorKind = PrincipalKind.AdminUser })).Total.ShouldBe(5);
    }

    [Fact]
    public async Task ItFiltersByOrigin()
    {
        using var database = Seeded();

        (await ActionsAsync(database, new ActivityFilter { Origin = Origin.Admin })).ShouldBe(["content.published", "content.draft_saved"]);
        (await ActionsAsync(database, new ActivityFilter { Origin = Origin.Mcp })).ShouldBe(["content.published"]);
        (await ActionsAsync(database, new ActivityFilter { Origin = Origin.Cli })).ShouldBeEmpty();
    }

    [Fact]
    public async Task ItFiltersByActionExactlyOrByPrefix()
    {
        using var database = Seeded();

        (await ActionsAsync(database, new ActivityFilter { Action = "content.published" })).Count.ShouldBe(2);
        (await ActionsAsync(database, new ActivityFilter { Action = "content.*" })).Count.ShouldBe(3);
        (await ActionsAsync(database, new ActivityFilter { Action = "content.pub" })).ShouldBeEmpty();
        (await ActionsAsync(database, new ActivityFilter { Action = "*" })).Count.ShouldBe(5);
    }

    [Fact]
    public async Task ItFiltersByTarget()
    {
        using var database = Seeded();

        (await ActionsAsync(database, new ActivityFilter { TargetType = "article" })).Count.ShouldBe(3);
        (await ActionsAsync(database, new ActivityFilter { TargetType = "article", TargetId = First.ToString("D") })).ShouldBe(["content.published", "content.draft_saved"]);
        (await ActionsAsync(database, new ActivityFilter { TargetId = "seo" })).ShouldBe(["settings.changed"]);
    }

    [Fact]
    public async Task ItFiltersByDateRangeFromInclusiveToExclusive()
    {
        using var database = Seeded();

        (await ActionsAsync(database, new ActivityFilter { From = Noon.AddMinutes(1), To = Noon.AddHours(1) })).ShouldBe(["content.published", "content.published"]);
        (await ActionsAsync(database, new ActivityFilter { From = Noon.AddHours(1) })).ShouldBe(["settings.changed", "site.updated"]);
        (await ActionsAsync(database, new ActivityFilter { To = Noon.AddMinutes(1) })).ShouldBe(["content.draft_saved"]);
        (await ActionsAsync(database, new ActivityFilter { From = Noon.ToOffset(TimeSpan.FromHours(5)).AddMinutes(2), To = Noon.AddMinutes(3) })).ShouldBe(["content.published"]);
    }

    [Fact]
    public async Task AFilterThatIsSetMustMatchTogetherWithTheOthers()
    {
        using var database = Seeded();

        var filter = new ActivityFilter { ActorId = Ann, Origin = Origin.Admin, Action = "content.*", TargetType = "article", TargetId = First.ToString("D"), From = Noon, To = Noon.AddMinutes(2) };

        (await ActionsAsync(database, filter)).ShouldBe(["content.published", "content.draft_saved"]);
        (await ActionsAsync(database, filter with { Origin = Origin.Mcp })).ShouldBeEmpty();
    }

    [Fact]
    public async Task TheRowsOfNoSiteAreLeftOutUnlessAsked()
    {
        using var database = Seeded();

        (await ActionsAsync(database, new ActivityFilter { Origin = Origin.Job })).ShouldBeEmpty();

        var page = await QueryAsync(database, new ActivityFilter { Origin = Origin.Job, IncludeInstance = true });
        var item = page.Items.ShouldHaveSingleItem();
        item.Action.ShouldBe("job.failed");
        item.SiteId.ShouldBeNull();
    }

    [Fact]
    public async Task ItPagesByNumberOrOffsetAndCountsUnlessAskedNotTo()
    {
        using var database = Seeded();

        var second = await QueryAsync(database, page: new PageRequest { Number = 2, Size = 2 });
        second.Items.Select(item => item.Action).ShouldBe(["content.published", "content.published"]);
        second.Total.ShouldBe(5);
        second.Page.ShouldBe(2);
        second.PageSize.ShouldBe(2);
        second.NextCursor.ShouldBeNull();

        var offset = await QueryAsync(database, page: new PageRequest { Offset = 4, Size = 2 });
        offset.Items.ShouldHaveSingleItem().Action.ShouldBe("content.draft_saved");
        offset.Page.ShouldBeNull();

        (await QueryAsync(database, page: new PageRequest { WithTotal = false })).Total.ShouldBeNull();
        (await QueryAsync(database)).PageSize.ShouldBe(50);
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(101, null, null)]
    [InlineData(null, 0, null)]
    [InlineData(null, null, -1)]
    [InlineData(10, 1, 1)]
    public async Task APageThatCannotBeReadIsRefused(int? size, int? number, int? offset)
    {
        using var database = Seeded();

        var exception = await Should.ThrowAsync<ArgumentException>(() => QueryAsync(database, page: new PageRequest { Size = size, Number = number, Offset = offset }));

        exception.Message.ShouldStartWith("FCMS2703");
    }

    [Fact]
    public async Task ItNeedsAnOperationToKnowTheSite()
    {
        using var database = Seeded();
        using var scope = database.Database.NewScope();

        await Should.ThrowAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IActivityQuery>().QueryAsync(null, null, Token));
    }

    [Fact]
    public async Task ARowComesBackWithEverythingItStored()
    {
        using var database = new ServiceDatabase();
        var row = Row("content.published", SiteA.Value, Noon, Ann, Origin.Admin, "article", First.ToString("D"));
        row.ClientId = "client-1";
        row.TargetLabel = "label";
        row.Locale = "fr";
        row.Details = "{\"version\":\"3\"}";
        row.Ip = "10.0.0.1";
        database.Add(row);

        var item = (await QueryAsync(database)).Items.Single();

        item.ShouldBe(new ActivityItem(row.Id, SiteA.Value, Noon, PrincipalKind.AdminUser, Ann, "Someone", Origin.Admin, "client-1", "content.published", "article", First.ToString("D"), "label", "fr", item.Details, "c", "10.0.0.1"));
        item.Details["version"].ShouldBe("3");
    }
}
