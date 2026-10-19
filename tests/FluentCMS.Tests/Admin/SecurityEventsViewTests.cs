using Bunit;
using FluentCMS.Admin.Components.Layout;
using FluentCMS.Admin.Components.Pages;
using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Desk;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Admin;

public sealed class SecurityEventsViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Lists_each_event_with_what_happened_who_did_it_and_what_it_was_about()
    {
        var lists = new FakeLists();
        using var context = Context(lists);

        var cut = context.Render<SecurityEventsView>();

        var rows = cut.FindAll("[data-event]");
        Assert.Equal(["agent_credential.revoked", "agent.calls_per_period_exceeded", "routing_rule.activated"], rows.Select(row => row.GetAttribute("data-event")));
        Assert.Contains("An agent key was revoked", rows[0].TextContent);
        Assert.Equal("user", rows[0].QuerySelector("[data-initiator]")!.GetAttribute("data-initiator"));
        Assert.Equal("agent", rows[1].QuerySelector("[data-initiator]")!.GetAttribute("data-initiator"));
        Assert.Equal($"audit?type=agent_credential&id={lists.Subject}", rows[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("2026-10-18 12:00:00 UTC", rows[0].QuerySelector("time")!.TextContent);
        Assert.Equal(SecurityEvents.Operations, Assert.Single(lists.Queries).Operations);
        Assert.Equal((0, SecurityEventsView.PageSize), (lists.Queries[0].Offset, lists.Queries[0].Limit));
        Assert.Equal("rove", cut.Find("tbody").GetAttribute("data-ui"));
    }

    [Fact]
    public void The_page_comes_from_the_address_and_links_to_its_neighbours()
    {
        var lists = new FakeLists { Total = 160 };
        using var context = Context(lists);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/security?page=2");

        var cut = context.Render<SecurityEventsView>();

        Assert.Equal(50, lists.Queries[0].Offset);
        Assert.Contains("51 to 53 of 160", cut.Find("[data-testid=security-pages]").TextContent);
        Assert.Equal("security", cut.Find("a[rel=prev]").GetAttribute("href"));
        Assert.Equal("security?page=3", cut.Find("a[rel=next]").GetAttribute("href"));
    }

    [Fact]
    public void An_empty_log_says_so()
    {
        using var context = Context(new FakeLists { Total = 0, Empty = true });

        var cut = context.Render<SecurityEventsView>();

        Assert.NotNull(cut.Find("[data-testid=security-empty]"));
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void The_header_shows_the_awaiting_and_security_counts_only_when_there_is_something()
    {
        var lists = new FakeLists();
        var queues = new CountingQueues { AwaitingMe = 4 };
        using var context = Context(lists, queues);

        var cut = context.Render<NotificationLinks>();

        Assert.Equal("4", cut.Find("[data-testid=nav-desk] [data-count]").GetAttribute("data-count"));
        Assert.Equal("3", cut.Find("[data-testid=nav-security] [data-count]").GetAttribute("data-count"));
        var query = Assert.Single(lists.Counted);
        Assert.Equal(Now.AddDays(-1), query.Since);

        queues.AwaitingMe = 0;
        lists.Total = 0;
        var quiet = context.Render<NotificationLinks>();
        Assert.Empty(quiet.FindAll("[data-count]"));
    }

    private static AdminTestContext Context(FakeLists lists, CountingQueues? queues = null)
    {
        var context = new AdminTestContext();
        context.Services.AddSingleton<IAuditEventLists>(lists);
        context.Services.AddSingleton<IDeskQueues>(queues ?? new CountingQueues());
        context.Services.AddSingleton<TimeProvider>(new FixedClock(Now));
        return context;
    }

    private sealed class FakeLists : IAuditEventLists
    {
        public Guid Subject { get; } = Guid.NewGuid();

        public int Total { get; set; } = 3;

        public bool Empty { get; init; }

        public List<AuditEventListQuery> Queries { get; } = [];

        public List<AuditEventListQuery> Counted { get; } = [];

        public Task<Result<AuditEventList>> ListAsync(AuditEventListQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            var person = new UserInitiator(Guid.NewGuid(), "Dana");
            var agent = new AgentInitiator(Guid.NewGuid(), "catalog-sync");
            AuditEvent[] events = Empty
                ? []
                :
                [
                    Event("agent_credential.revoked", person, 0),
                    Event("agent.calls_per_period_exceeded", agent, -5),
                    Event("routing_rule.activated", person, -10),
                ];
            return Task.FromResult(Result<AuditEventList>.Success(new AuditEventList(events, Total)));
        }

        public Task<Result<int>> CountAsync(AuditEventListQuery query, CancellationToken cancellationToken = default)
        {
            Counted.Add(query);
            return Task.FromResult(Result<int>.Success(Total));
        }

        private AuditEvent Event(string operation, Initiator initiator, int minutes)
        {
            return new AuditEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = Now.AddMinutes(minutes),
                Initiator = initiator,
                Operation = operation,
                SubjectType = "agent_credential",
                SubjectId = Subject,
            };
        }
    }

    private sealed class CountingQueues : IDeskQueues
    {
        public int AwaitingMe { get; set; }

        public Task<Result<int>> CountAsync(DeskQueue queue, CancellationToken cancellationToken = default)
        {
            Assert.Equal(DeskQueue.AwaitingMe, queue);
            return Task.FromResult(Result<int>.Success(AwaitingMe));
        }

        public Task<Result<IReadOnlyDictionary<DeskQueue, int>>> CountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ChangeSetList>> ListChangeSetsAsync(DeskQueue queue, int offset, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<StaleFactList>> ListStaleFactsAsync(int offset, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
