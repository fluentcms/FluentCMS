using Bunit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Desk;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using DeskPage = FluentCMS.Admin.Components.Pages.Desk;

namespace FluentCMS.Tests.Admin;

public sealed class DeskTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Lists_the_six_queues_with_their_counts_and_opens_on_awaiting_me()
    {
        using var context = Context(new FakeQueues());

        var cut = context.Render<DeskPage>();

        var links = cut.FindAll("[data-testid=desk-queues] a");
        Assert.Equal(
            ["awaiting-me", "awaiting-anyone", "checks-failed", "stale-facts", "recently-published", "reverted"],
            links.Select(link => link.GetAttribute("data-queue")));
        Assert.Equal(["2", "2", "1", "3", "4", "0"], links.Select(link => link.QuerySelector("[data-count]")!.TextContent));
        Assert.Equal("?queue=checks-failed", links[2].GetAttribute("href"));
        Assert.Equal("page", links[0].GetAttribute("aria-current"));
        Assert.All(links.Skip(1), link => Assert.False(link.HasAttribute("aria-current")));
        Assert.Equal("awaiting-me", cut.Find("[data-testid=desk-queue]").GetAttribute("data-current-queue"));
        Assert.NotNull(cut.Find("[data-testid=awaiting-me-note]"));
    }

    [Fact]
    public void A_set_shows_its_rationale_initiator_items_and_time_and_links_to_its_view()
    {
        var queues = new FakeQueues();
        using var context = Context(queues);

        var cut = context.Render<DeskPage>();

        var rows = cut.FindAll("[data-change-set]");
        Assert.Equal(2, rows.Count);
        Assert.Equal(queues.Agent.Id.ToString("N"), rows[0].GetAttribute("data-change-set"));
        var link = rows[0].QuerySelector("a")!;
        Assert.Equal("Sync prices from the catalog export.", link.TextContent);
        Assert.Equal($"sets/{queues.Agent.Id}?queue=awaiting-me", link.GetAttribute("href"));
        Assert.Equal("agent", rows[0].QuerySelector("[data-initiator]")!.GetAttribute("data-initiator"));
        Assert.Contains("catalog-sync", rows[0].TextContent);
        Assert.Contains("12", rows[0].QuerySelectorAll("td")[2].TextContent);
        Assert.Equal("2026-10-18T09:30:00.0000000+00:00", rows[0].QuerySelector("time")!.GetAttribute("datetime"));
        Assert.Contains("2026-10-18 09:30 UTC", rows[0].QuerySelector("time")!.TextContent);
        Assert.Null(rows[0].QuerySelector("[data-reverts]"));

        Assert.Equal("user", rows[1].QuerySelector("[data-initiator]")!.GetAttribute("data-initiator"));
        Assert.Equal(queues.Agent.Id.ToString("N"), rows[1].QuerySelector("[data-reverts]")!.GetAttribute("data-reverts"));
        Assert.Equal([(DeskQueue.AwaitingMe, 0, DeskPage.PageSize)], queues.SetPages);
    }

    [Fact]
    public void Rows_rove_with_only_the_first_in_the_tab_order_and_queues_have_number_keys()
    {
        using var context = Context(new FakeQueues());

        var cut = context.Render<DeskPage>();

        Assert.Equal("rove", cut.Find("tbody").GetAttribute("data-ui"));
        var links = cut.FindAll("tbody [data-ui=rove-item]");
        Assert.Equal(2, links.Count);
        Assert.Equal([null, "-1"], links.Select(link => link.GetAttribute("tabindex")));
        Assert.Equal(["1", "2", "3", "4", "5", "6"], cut.FindAll("[data-queue]").Select(link => link.GetAttribute("aria-keyshortcuts")));
        Assert.Equal("?", cut.Find("[data-testid=shortcut-help]").GetAttribute("aria-keyshortcuts"));
        Assert.Equal(6, cut.FindAll("[data-testid=shortcut-list] dt").Count);
        Assert.Equal("Home End", string.Join(" ", cut.FindAll("[data-testid=shortcut-list] dt")[3].QuerySelectorAll("kbd").Select(key => key.TextContent)));
        Assert.Equal("single-key-shortcuts", cut.Find("[role=switch]").GetAttribute("data-ui"));
        Assert.True(cut.Find("[role=switch]").HasAttribute("checked"));
    }

    [Theory]
    [InlineData("approved", "The set was approved. This queue has nothing more to review.")]
    [InlineData("rejected", "The set was rejected. This queue has nothing more to review.")]
    public void Says_what_the_set_view_did_when_it_comes_back_to_an_empty_queue(string done, string message)
    {
        using var context = Context(new FakeQueues());
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/?queue=awaiting-me&done={done}");

        var cut = context.Render<DeskPage>();

        Assert.Equal(message, cut.Find("[data-testid=desk-done]").TextContent);
    }

    [Fact]
    public void The_queue_and_page_come_from_the_address()
    {
        var queues = new FakeQueues { Total = 120 };
        using var context = Context(queues);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/?queue=recently-published&page=2");

        var cut = context.Render<DeskPage>();

        Assert.Equal([(DeskQueue.RecentlyPublished, 50, 50)], queues.SetPages);
        Assert.Equal("recently-published", cut.Find("[data-testid=desk-queue]").GetAttribute("data-current-queue"));
        Assert.Equal("page", cut.Find("[data-queue=recently-published]").GetAttribute("aria-current"));
        Assert.Empty(cut.FindAll("[data-testid=awaiting-me-note]"));
        Assert.Contains("51 to 52 of 120", cut.Find("[data-testid=desk-pages]").TextContent);
        Assert.Equal("?queue=recently-published", cut.Find("a[rel=prev]").GetAttribute("href"));
        Assert.Equal("?queue=recently-published&page=3", cut.Find("a[rel=next]").GetAttribute("href"));
        Assert.Contains("Published", cut.FindAll("th")[3].TextContent);
    }

    [Theory]
    [InlineData("/?queue=nonsense", DeskQueue.AwaitingMe, 0)]
    [InlineData("/?queue=reverted&page=0", DeskQueue.Reverted, 0)]
    [InlineData("/?queue=reverted&page=-3", DeskQueue.Reverted, 0)]
    [InlineData("/?queue=reverted&page=abc", DeskQueue.Reverted, 0)]
    [InlineData("/?queue=reverted&page=99999999999", DeskQueue.Reverted, 0)]
    [InlineData("/?queue=reverted&page=", DeskQueue.Reverted, 0)]
    public void An_unknown_queue_or_page_falls_back_to_the_first(string address, DeskQueue queue, int offset)
    {
        var queues = new FakeQueues();
        using var context = Context(queues);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(address);

        context.Render<DeskPage>();

        Assert.Equal([(queue, offset, DeskPage.PageSize)], queues.SetPages);
    }

    [Fact]
    public void The_last_page_has_no_next_link_and_the_first_no_previous_link()
    {
        using var context = Context(new FakeQueues());

        var cut = context.Render<DeskPage>();

        Assert.Contains("1 to 2 of 2", cut.Find("[data-testid=desk-pages]").TextContent);
        Assert.Empty(cut.FindAll("a[rel=prev]"));
        Assert.Empty(cut.FindAll("a[rel=next]"));
    }

    [Fact]
    public void A_page_past_the_end_links_back_to_the_first()
    {
        var queues = new FakeQueues { Total = 2 };
        using var context = Context(queues);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/?queue=awaiting-anyone&page=9");
        queues.Empty = true;

        var cut = context.Render<DeskPage>();

        Assert.Equal("?queue=awaiting-anyone", cut.Find("[data-testid=desk-past-end] a").GetAttribute("href"));
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void An_empty_queue_says_so()
    {
        var queues = new FakeQueues { Total = 0, Empty = true };
        using var context = Context(queues);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/?queue=checks-failed");

        var cut = context.Render<DeskPage>();

        Assert.Equal("No change set has failed its checks.", cut.Find("[data-testid=desk-empty]").TextContent);
        Assert.Empty(cut.FindAll("table"));
        Assert.Empty(cut.FindAll("[data-testid=desk-pages]"));
    }

    [Fact]
    public void The_stale_facts_queue_lists_each_fact_with_its_reason_and_links_to_its_entity()
    {
        var queues = new FakeQueues();
        using var context = Context(queues);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/?queue=stale-facts");

        var cut = context.Render<DeskPage>();

        var rows = cut.FindAll("[data-fact]");
        Assert.Equal(2, rows.Count);
        Assert.Equal("expired", rows[0].QuerySelector("[data-reason]")!.GetAttribute("data-reason"));
        Assert.Equal("unverified", rows[1].QuerySelector("[data-reason]")!.GetAttribute("data-reason"));
        Assert.Equal($"audit?type=entity&id={queues.Entity}", rows[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("price", rows[0].TextContent);
        Assert.Contains("Published", rows[0].TextContent);
        Assert.DoesNotContain("Latest draft", rows[0].TextContent);
        Assert.Contains("Latest draft", rows[1].TextContent);
        Assert.Equal([(0, DeskPage.PageSize)], queues.FactPages);
        Assert.Empty(queues.SetPages);
    }

    [Fact]
    public void A_refused_read_is_reported_in_place_of_the_list()
    {
        var queues = new FakeQueues { Refuse = true };
        using var context = Context(queues);

        var cut = context.Render<DeskPage>();

        // The code is the resource key, which the test localizer returns as the text.
        Assert.Equal(ChangeSetErrorCodes.LimitInvalid, cut.Find("[data-testid=desk-error]").TextContent);
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void Uses_logical_utilities_only()
    {
        using var context = Context(new FakeQueues());

        var cut = context.Render<DeskPage>();

        var classes = cut.FindAll("[class]").SelectMany(element => element.ClassList);
        Assert.DoesNotContain(classes, Utilities.IsPhysical);
    }

    private static AdminTestContext Context(FakeQueues queues)
    {
        var context = new AdminTestContext();
        context.Services.AddSingleton<IDeskQueues>(queues);
        return context;
    }

    private sealed class FakeQueues : IDeskQueues
    {
        public ChangeSetSummary Agent { get; } = new(
            Guid.CreateVersion7(),
            "Sync prices from the catalog export.",
            new AgentInitiator(Guid.NewGuid(), "catalog-sync"),
            ChangeSetState.AwaitingReview,
            12,
            null,
            Now.AddHours(-1),
            Now);

        public Guid Entity { get; } = Guid.NewGuid();

        public int Total { get; init; } = 2;

        public bool Empty { get; set; }

        public bool Refuse { get; init; }

        public List<(DeskQueue Queue, int Offset, int Limit)> SetPages { get; } = [];

        public List<(int Offset, int Limit)> FactPages { get; } = [];

        public Task<Result<IReadOnlyDictionary<DeskQueue, int>>> CountAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<DeskQueue, int> counts = new Dictionary<DeskQueue, int>
            {
                [DeskQueue.AwaitingMe] = 2,
                [DeskQueue.AwaitingAnyone] = 2,
                [DeskQueue.ChecksFailed] = 1,
                [DeskQueue.StaleFacts] = 3,
                [DeskQueue.RecentlyPublished] = 4,
                [DeskQueue.Reverted] = 0,
            };
            return Task.FromResult(Result<IReadOnlyDictionary<DeskQueue, int>>.Success(counts));
        }

        public Task<Result<int>> CountAsync(DeskQueue queue, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ChangeSetList>> ListChangeSetsAsync(DeskQueue queue, int offset, int limit, CancellationToken cancellationToken = default)
        {
            SetPages.Add((queue, offset, limit));
            if (Refuse)
            {
                return Task.FromResult(Result<ChangeSetList>.Failure(ChangeSetErrorCodes.LimitInvalid, "The limit is out of range."));
            }

            var revert = Agent with
            {
                Id = Guid.CreateVersion7(),
                Rationale = "Undo the price sync.",
                Initiator = new UserInitiator(Guid.NewGuid(), "Amir"),
                ItemCount = 1,
                RevertsChangeSetId = Agent.Id,
            };
            return Task.FromResult(Result<ChangeSetList>.Success(new ChangeSetList(Empty ? [] : [Agent, revert], Total)));
        }

        public Task<Result<StaleFactList>> ListStaleFactsAsync(int offset, int limit, CancellationToken cancellationToken = default)
        {
            FactPages.Add((offset, limit));
            StaleFact[] facts =
            [
                new(Entity, "product", "price", 3, false, true, StaleReason.Expired, Now.AddDays(-20)),
                new(Entity, "product", "price", 4, true, false, StaleReason.Unverified, Now.AddDays(-2)),
            ];
            return Task.FromResult(Result<StaleFactList>.Success(new StaleFactList(facts, facts.Length)));
        }
    }
}
