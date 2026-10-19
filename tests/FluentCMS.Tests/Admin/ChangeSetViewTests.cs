using Bunit;
using FluentCMS.Admin.Components.Pages;
using FluentCMS.Admin.Components.Review;
using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Desk;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Admin;

public sealed class ChangeSetViewTests
{
    private static readonly DateTimeOffset Opened = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Shows_the_rationale_state_initiator_and_times_from_the_history()
    {
        var sets = new FakeSets();
        using var context = Context(sets);

        var cut = context.Render<ChangeSetView>(page => page.Add(p => p.Id, sets.Set.Id));

        Assert.Equal("Sync prices from the catalog export.", cut.Find("h1").TextContent);
        Assert.Equal("awaiting-review", cut.Find("[data-testid=change-set]").GetAttribute("data-state"));
        Assert.Equal("Awaiting review", cut.Find("[data-testid=change-set-state]").TextContent);
        Assert.Equal("agent", cut.Find("[data-initiator]").GetAttribute("data-initiator"));
        Assert.Contains("catalog-sync", cut.Find("[data-testid=change-set]").TextContent);
        var times = cut.FindAll("[data-testid=change-set] time").Select(time => time.TextContent).ToList();
        Assert.Equal(["2026-10-18 09:00 UTC", "2026-10-18 09:10 UTC"], times);
        Assert.Empty(cut.FindAll("[data-testid=change-set-reverts]"));
        Assert.Empty(cut.FindAll("[data-testid=change-set-rejection]"));
    }

    [Fact]
    public void Only_web_addresses_among_the_sources_are_links_that_open_in_a_new_tab()
    {
        var sets = new FakeSets();
        using var context = Context(sets);

        var cut = context.Render<ChangeSetView>(page => page.Add(p => p.Id, sets.Set.Id));

        var sources = cut.FindAll("[data-testid=change-set-sources] li");
        Assert.Equal(4, sources.Count);
        var link = Assert.Single(cut.FindAll("[data-testid=change-set-sources] a"));
        Assert.Equal("https://example.com/catalog", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.Equal("exports/catalog-2026-10-18.csv", sources[1].TextContent.Trim());
        Assert.Equal("javascript:alert(1)", sources[2].TextContent.Trim());
        Assert.Equal("Dana in sales", sources[3].TextContent.Trim());
    }

    [Theory]
    [InlineData("https://example.com/a?b=c", "https://example.com/a?b=c")]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("file:///etc/passwd", null)]
    [InlineData("mailto:someone@example.com", null)]
    [InlineData("example.com/page", null)]
    public void A_source_opens_only_as_a_web_address(string source, string? address)
    {
        Assert.Equal(address, ChangeSetView.Openable(source));
    }

    [Fact]
    public void Lists_the_history_oldest_first_with_who_moved_the_set()
    {
        var sets = new FakeSets();
        using var context = Context(sets);

        var cut = context.Render<ChangeSetView>(page => page.Add(p => p.Id, sets.Set.Id));

        var events = cut.FindAll("[data-testid=change-set-history] li");
        Assert.Equal(["change_set.opened", "change_set.submitted", "agent.transition_refused", "change_set.awaiting_review"], events.Select(item => item.GetAttribute("data-operation")));
        Assert.Contains("Opened", events[0].TextContent);
        Assert.Contains("agent.transition_refused", events[2].TextContent);
        Assert.Contains("Amir", events[3].TextContent);
    }

    [Fact]
    public void A_revert_set_links_to_the_set_it_reverts_and_a_rejected_set_shows_its_reason()
    {
        var reverted = Guid.NewGuid();
        var sets = new FakeSets { RevertsChangeSetId = reverted, State = ChangeSetState.Rejected, RejectionReason = "The prices are from last year." };
        using var context = Context(sets);

        var cut = context.Render<ChangeSetView>(page => page.Add(p => p.Id, sets.Set.Id));

        Assert.Equal($"sets/{reverted}", cut.Find("[data-testid=change-set-reverts] a").GetAttribute("href"));
        Assert.Contains("The prices are from last year.", cut.Find("[data-testid=change-set-rejection]").TextContent);
        Assert.Equal("rejected", cut.Find("[data-testid=change-set]").GetAttribute("data-state"));
    }

    [Fact]
    public void The_checks_card_shows_the_latest_run_with_failing_results_first_and_their_evidence()
    {
        var sets = new FakeSets { State = ChangeSetState.ChecksFailed };
        using var context = Context(sets);

        var cut = context.Render<ChangeSetView>(page => page.Add(p => p.Id, sets.Set.Id));

        var card = cut.Find("[data-testid=change-set-checks]");
        Assert.Equal("fail", card.QuerySelector("[data-testid=check-run-status]")!.GetAttribute("data-status"));
        Assert.Equal(["no-superlatives", "short-headline"], card.QuerySelectorAll("[data-check]").Select(check => check.GetAttribute("data-check")));
        var finding = card.QuerySelector("[data-check=no-superlatives] [data-finding]")!;
        Assert.Contains("Remove 'best in class'.", finding.TextContent);
        Assert.Equal("/headline", finding.QuerySelector("code")!.TextContent);
        Assert.Equal("Best in class", finding.QuerySelector("q")!.TextContent);
        Assert.Contains("The engine could not read one item.", card.QuerySelector("[data-testid=check-problems]")!.TextContent);
        Assert.Contains("Routing has not decided on this set.", cut.Find("[data-testid=change-set-routing]").TextContent);
    }

    [Fact]
    public void A_set_still_being_checked_shows_the_job_progress()
    {
        var sets = new FakeSets { State = ChangeSetState.ChecksRunning };
        using var context = Context(sets);

        var cut = context.Render<ChangeSetView>(page => page.Add(p => p.Id, sets.Set.Id));

        Assert.Contains("3 of 10 items done", cut.Find("[data-testid=change-set-checks]").TextContent);
    }

    [Theory]
    [InlineData(ChangeSetState.AwaitingReview, new[] { "approve", "reject" })]
    [InlineData(ChangeSetState.ChecksFailed, new[] { "reject" })]
    [InlineData(ChangeSetState.Approved, new[] { "publish", "reject" })]
    [InlineData(ChangeSetState.Published, new[] { "revert" })]
    [InlineData(ChangeSetState.Open, new[] { "submit" })]
    [InlineData(ChangeSetState.ChecksRunning, new string[0])]
    [InlineData(ChangeSetState.Rejected, new string[0])]
    [InlineData(ChangeSetState.Reverted, new string[0])]
    public void The_review_panel_offers_only_the_actions_the_state_allows(ChangeSetState state, string[] actions)
    {
        var sets = new FakeSets { State = state };
        using var context = Context(sets);

        var cut = context.Render<ChangeSetView>(page => page.Add(p => p.Id, sets.Set.Id));

        var review = cut.Find("[data-testid=change-set-review]");
        Assert.Equal(actions, review.QuerySelectorAll("[data-action]").Select(action => action.GetAttribute("data-action")));
        Assert.NotEmpty(review.QuerySelector("[data-slot=card-description]")!.TextContent.Trim());

        // Every action's form is on the page, so a post from an older page still reaches its handler; only the offered ones show.
        Assert.Equal(5, review.QuerySelectorAll("form").Length);
        Assert.Equal(5 - actions.Length, review.QuerySelectorAll("form[hidden]").Length);
        Assert.All(
            review.QuerySelectorAll("input[name$='.Seen']"),
            seen => Assert.Matches("^[0-9a-f]{32}$", seen.GetAttribute("value")));
    }

    [Fact]
    public void The_actions_have_their_keys_and_a_set_opened_from_a_queue_links_back_and_to_the_next_set()
    {
        var sets = new FakeSets();
        using var context = Context(sets);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/sets/{sets.Set.Id}?queue=awaiting-anyone&done=approved");
        var next = ((QueueAfter)context.Services.GetRequiredService<IDeskQueues>()).Next;

        var cut = context.Render<ChangeSetView>(page => page.Add(p => p.Id, sets.Set.Id));

        Assert.Equal("A", cut.Find("[data-action=approve]").GetAttribute("aria-keyshortcuts"));
        Assert.Equal("R", cut.Find("[data-action=reject]").GetAttribute("aria-keyshortcuts"));
        Assert.Equal("?queue=awaiting-anyone", cut.Find("[data-testid=back-to-queue]").GetAttribute("href"));
        Assert.Equal("B", cut.Find("[data-testid=back-to-queue]").GetAttribute("aria-keyshortcuts"));
        Assert.Equal($"sets/{next}?queue=awaiting-anyone", cut.Find("[data-testid=next-in-queue]").GetAttribute("href"));
        Assert.Equal("N", cut.Find("[data-testid=next-in-queue]").GetAttribute("aria-keyshortcuts"));
        Assert.Equal("The previous set was approved.", cut.Find("[data-testid=review-done]").TextContent);
        Assert.Equal(9, cut.FindAll("[data-testid=shortcut-list] dt").Count);
    }

    [Fact]
    public void A_set_opened_without_a_queue_links_back_to_the_desk_and_has_no_next()
    {
        var sets = new FakeSets();
        using var context = Context(sets);

        var cut = context.Render<ChangeSetView>(page => page.Add(p => p.Id, sets.Set.Id));

        Assert.Equal("", cut.Find("[data-testid=back-to-queue]").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid=next-in-queue]"));
        Assert.Empty(cut.FindAll("[data-testid=review-done]"));
    }

    [Fact]
    public void A_rejection_asks_for_its_reason_in_a_dialog()
    {
        var sets = new FakeSets();
        using var context = Context(sets);

        var cut = context.Render<ChangeSetView>(page => page.Add(p => p.Id, sets.Set.Id));

        var reason = cut.Find("dialog textarea[name='Reject.Reason']");
        Assert.True(reason.HasAttribute("required"));
        Assert.Equal("dialog", cut.Find("[data-action=reject]").GetAttribute("aria-haspopup"));
    }

    [Fact]
    public void An_unknown_set_is_not_found()
    {
        using var context = Context(new FakeSets());

        var cut = context.Render<ChangeSetView>(page => page.Add(p => p.Id, Guid.NewGuid()));

        Assert.Empty(cut.FindAll("h1"));
        Assert.Empty(cut.FindAll("[data-testid=change-set-items]"));
    }

    [Fact]
    public void The_items_island_renders_the_diff_of_the_set()
    {
        var sets = new FakeSets();
        using var context = Context(sets);

        var cut = context.Render<ChangeSetItems>(items => items.Add(p => p.ChangeSetId, sets.Set.Id).SetAssignedRenderMode(RenderMode.InteractiveServer));

        Assert.Equal(2, cut.FindAll("[data-slot=diff-view-item]").Count);
        Assert.Equal([sets.Set.Id], sets.Diffed);
        Assert.Equal("rove", cut.Find("ol").GetAttribute("data-ui"));
        Assert.Equal("false", cut.Find("[data-testid=change-set-items]").GetAttribute("data-interactive"));

        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var live = context.Render<ChangeSetItems>(items => items.Add(p => p.ChangeSetId, sets.Set.Id));
        Assert.Equal("true", live.Find("[data-testid=change-set-items]").GetAttribute("data-interactive"));
    }

    [Fact]
    public void The_items_island_reports_a_refused_diff()
    {
        var sets = new FakeSets();
        using var context = Context(sets);

        var cut = context.Render<ChangeSetItems>(items => items.Add(p => p.ChangeSetId, Guid.NewGuid()));

        Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, cut.Find("[role=alert]").TextContent);
        Assert.Empty(cut.FindAll("[data-slot=diff-view]"));
    }

    [Fact]
    public void Uses_logical_utilities_only()
    {
        var sets = new FakeSets { RevertsChangeSetId = Guid.NewGuid() };
        using var context = Context(sets);

        var cut = context.Render<ChangeSetView>(page => page.Add(p => p.Id, sets.Set.Id));

        Assert.DoesNotContain(cut.FindAll("[class]").SelectMany(element => element.ClassList), Utilities.IsPhysical);
    }

    private static AdminTestContext Context(FakeSets sets)
    {
        var context = new AdminTestContext();
        context.Services.AddSingleton<IChangeSetDirectory>(sets);
        context.Services.AddSingleton<IAuditReader>(new FakeHistory(sets));
        context.Services.AddSingleton<IDeskQueues>(new QueueAfter(sets));
        context.Services.AddSingleton<IChangeSetChecks>(new FakeChecks(sets));
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        return context;
    }

    private sealed class FakeSets : IChangeSetDirectory
    {
        private readonly Guid _id = Guid.CreateVersion7();

        public ChangeSetState State { get; init; } = ChangeSetState.AwaitingReview;

        public Guid? RevertsChangeSetId { get; init; }

        public string? RejectionReason { get; init; }

        public List<Guid> Diffed { get; } = [];

        public ChangeSet Set => ChangeSet.Restore(
            _id,
            Guid.NewGuid(),
            "Sync prices from the catalog export.",
            ["https://example.com/catalog", "exports/catalog-2026-10-18.csv", "javascript:alert(1)", "Dana in sales"],
            new AgentInitiator(Guid.NewGuid(), "catalog-sync"),
            null,
            Operations,
            State,
            RejectionReason,
            RevertsChangeSetId);

        private static ChangeOperation[] Operations =>
        [
            ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, Guid.NewGuid(), null, 1).Value,
            ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, Guid.NewGuid(), 1, 2).Value,
        ];

        public Task<Result<ChangeSet>> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(id == _id
                ? Result<ChangeSet>.Success(Set)
                : Result<ChangeSet>.Failure(ChangeSetErrorCodes.ChangeSetNotFound, "No such set."));
        }

        public Task<Result<ChangeSetDiff>> DiffAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Diffed.Add(id);
            if (id != _id)
            {
                return Task.FromResult(Result<ChangeSetDiff>.Failure(ChangeSetErrorCodes.ChangeSetNotFound, "No such set."));
            }

            var items = Operations.Select(operation => ItemDiff.ForSchema(
                operation,
                operation.BaseVersion is null ? null : new FluentCMS.Core.Schema.ContentTypeDefinition([new("title", "text")]),
                new FluentCMS.Core.Schema.ContentTypeDefinition([new("title", "text"), new("summary", "text")]))).ToList();
            return Task.FromResult(Result<ChangeSetDiff>.Success(new ChangeSetDiff(id, items)));
        }

        public Task<Result<ChangeSet>> GetSessionSetAsync(string sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ChangeSet>> OpenAsync(NewChangeSet changeSet, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ChangeSet>> AddOperationAsync(Guid changeSetId, ChangeOperation operation, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ChangeSet>> AddToSessionAsync(string sessionId, NewChangeSet changeSet, ChangeOperation operation, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ChangeSet>> TransitionAsync(Guid changeSetId, ChangeSetState target, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ChangeSet>> PublishAsync(Guid changeSetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ChangeSet>> RevertAsync(Guid changeSetId, string rationale, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ChangeSet>> RejectAsync(Guid changeSetId, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // A queue holding the set and one more after it.
    private sealed class QueueAfter(FakeSets sets) : IDeskQueues
    {
        public Guid Next { get; } = Guid.NewGuid();

        public Task<Result<IReadOnlyDictionary<DeskQueue, int>>> CountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> CountAsync(DeskQueue queue, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ChangeSetList>> ListChangeSetsAsync(DeskQueue queue, int offset, int limit, CancellationToken cancellationToken = default)
        {
            var current = new ChangeSetSummary(sets.Set.Id, "This set.", sets.Set.Initiator, ChangeSetState.AwaitingReview, 2, null, Opened, Opened);
            return Task.FromResult(Result<ChangeSetList>.Success(new ChangeSetList([current, current with { Id = Next }], 2)));
        }

        public Task<Result<StaleFactList>> ListStaleFactsAsync(int offset, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeChecks(FakeSets sets) : IChangeSetChecks
    {
        public Task<Result<ChangeSetChecksState>> GetAsync(Guid changeSetId, CancellationToken cancellationToken = default)
        {
            var job = new FluentCMS.Core.Jobs.Job
            {
                Id = Guid.NewGuid(),
                Type = "checks.run",
                Payload = default,
                State = FluentCMS.Core.Jobs.JobState.Running,
                Attempts = 1,
                MaxAttempts = 6,
                CreatedAt = Opened,
                ScheduledAt = Opened,
                Progress = new FluentCMS.Core.Jobs.JobProgress(3, 10, null),
            };
            if (sets.State is not ChangeSetState.ChecksFailed)
            {
                return Task.FromResult(Result<ChangeSetChecksState>.Success(new ChangeSetChecksState(sets.Set, null, sets.State == ChangeSetState.ChecksRunning ? job : null)));
            }

            CheckResult Result(string check, CheckStatus status, params CheckFinding[] findings) =>
                new(Guid.NewGuid(), check, 1, "terms", CheckKind.Deterministic, CheckSeverity.Block, "A check.", status, findings, null);
            var item = new ItemChecks(ItemKind.Entry, Guid.NewGuid(), 1, "product-page",
            [
                Result("short-headline", CheckStatus.Pass),
                Result("no-superlatives", CheckStatus.Fail, new CheckFinding("Remove 'best in class'.", "/headline", "Best in class")),
            ]);
            var run = new ChangeSetCheckRun(Guid.NewGuid(), sets.Set.Id, job.Id, 1, CheckRunMode.Inline, Opened, Opened, CheckStatus.Fail, 0m, [item], ["The engine could not read one item."]);
            return Task.FromResult(Result<ChangeSetChecksState>.Success(new ChangeSetChecksState(sets.Set, run, job)));
        }

        public Task<Result<ChangeSetSubmission>> SubmitAsync(Guid changeSetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ChangeSetCheckRun?>> RunAsync(Guid changeSetId, Guid jobId, CheckRunMode mode, Func<int, int, Task<bool>>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ChangeSetCheckRun?>> AbandonAsync(Guid changeSetId, Guid jobId, CheckRunMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeHistory(FakeSets sets) : IAuditReader
    {
        public Result<IAsyncEnumerable<AuditEvent>> Read(AuditQuery query)
        {
            Assert.Equal(new AuditSubject("change_set", sets.Set.Id), query.Subject);
            var agent = new AgentInitiator(Guid.NewGuid(), "catalog-sync");
            var person = new UserInitiator(Guid.NewGuid(), "Amir");
            AuditEvent[] events =
            [
                Event("change_set.opened", agent, 0),
                Event("change_set.submitted", agent, 5),
                Event("agent.transition_refused", agent, 7),
                Event("change_set.awaiting_review", person, 10),
            ];
            return Result<IAsyncEnumerable<AuditEvent>>.Success(events.ToAsyncEnumerable());
        }

        private static AuditEvent Event(string operation, Initiator initiator, int minutes)
        {
            return new AuditEvent { Id = Guid.NewGuid(), Timestamp = Opened.AddMinutes(minutes), Initiator = initiator, Operation = operation };
        }
    }
}
