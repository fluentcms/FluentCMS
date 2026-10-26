using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Desk;

public sealed partial class ReviewActionTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_reviewer_approves_and_then_publishes_a_set_and_each_step_names_them(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var set = await AwaitingReviewAsync(host, ownerId, "product");
        await host.SignInAsync(AdminHost.Owner);

        var approved = await ActAsync(host, set.Id, "approve");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(ChangeSetState.Approved, await StateAsync(host, ownerId, set.Id));
        var page = await approved.Content.ReadAsStringAsync();
        Assert.Contains("data-testid=\"review-message\"", page);
        Assert.Contains("data-state=\"approved\"", page);
        Assert.Contains("data-action=\"publish\"", page);

        var published = await (await ActAsync(host, set.Id, "publish")).Content.ReadAsStringAsync();
        Assert.Contains("data-state=\"published\"", published);
        Assert.Contains("data-action=\"revert\"", published);

        var moves = await MovesAsync(host, set.Id);
        Assert.Equal(["change_set.approved", "change_set.published"], moves.TakeLast(2).Select(row => row.Operation));
        Assert.All(moves.TakeLast(2), row => Assert.Equal((InitiatorKinds.User, ownerId), (row.InitiatorKind, row.InitiatorId)));
        Assert.Contains($"data-change-set=\"{set.Id:N}\"", await host.Client.GetStringAsync("/?queue=recently-published"));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Reviewing_from_a_queue_moves_on_to_its_next_set_and_back_to_the_queue_once_it_is_empty(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var first = await AwaitingReviewAsync(host, ownerId, "product");
        var second = await AwaitingReviewAsync(host, ownerId, "category");
        await host.SignInAsync(AdminHost.Owner);

        var approved = await ActAsync(host, first.Id, "approve", "?queue=awaiting-me");
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        Assert.Equal($"/sets/{second.Id}?queue=awaiting-me&done=approved", host.LocationOf(approved));

        var rejected = await ActAsync(host, second.Id, "reject", "?queue=awaiting-me", ("Reject.Reason", "Not needed."));
        Assert.Equal("/?queue=awaiting-me&done=rejected", host.LocationOf(rejected));
        Assert.Contains("data-testid=\"desk-done\"", await host.Client.GetStringAsync(host.LocationOf(rejected)!));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Submitting_an_open_set_runs_its_checks_and_the_view_shows_each_result_with_its_evidence(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await host.AsUserAsync<IContentTypeDirectory, ContentType>(ownerId, async types => (await types.CreateAsync(new NewContentType(
            "Product page", "product-page", new ContentTypeDefinition([new FieldDefinition("headline", "text", required: true)])), TestChangeSetTargets.ContentModel)).Value);
        await host.AsUserAsync<FluentCMS.Core.Checks.ICheckDirectory, bool>(ownerId, async checks =>
        {
            var definition = new FluentCMS.Core.Checks.CheckDefinition(
                "terms",
                "No superlatives.",
                JsonDocument.Parse("""{"terms": ["best in class"]}""").RootElement,
                [
                    new FluentCMS.Core.Checks.CheckExample("passes", FluentCMS.Core.Checks.CheckVerdict.Pass, JsonDocument.Parse("""{"headline": "A kettle"}""").RootElement, contentType: "product-page"),
                    new FluentCMS.Core.Checks.CheckExample("fails", FluentCMS.Core.Checks.CheckVerdict.Fail, JsonDocument.Parse("""{"headline": "Best in class"}""").RootElement, contentType: "product-page"),
                ],
                scope: new FluentCMS.Core.Checks.CheckScope(["product-page"]));
            Assert.True((await checks.CreateAsync(new FluentCMS.Core.Checks.NewCheck("no-superlatives", definition))).IsSuccess);
            return (await checks.ActivateAsync("no-superlatives", 1)).IsSuccess;
        });
        var entry = await host.AsUserAsync<FluentCMS.Core.Content.IEntryDirectory, FluentCMS.Core.Content.Entry>(ownerId, async entries => (await entries.CreateAsync(
            new FluentCMS.Core.Content.NewEntry("product-page", JsonDocument.Parse("""{"headline": "The best in class kettle"}""").RootElement))).Value);
        var set = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async directory =>
        {
            var opened = (await directory.OpenAsync(new NewChangeSet("Launch the kettle.", []))).Value;
            return (await directory.AddOperationAsync(opened.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Entry, entry.Id, null, 1).Value)).Value;
        });
        await host.SignInAsync(AdminHost.Owner);
        Assert.Equal(["submit"], Actions(await host.Client.GetStringAsync($"/sets/{set.Id}")));

        var submitted = await (await ActAsync(host, set.Id, "submit")).Content.ReadAsStringAsync();

        Assert.Contains("data-state=\"checks-failed\"", submitted);
        Assert.Contains("data-testid=\"check-run-status\" data-status=\"fail\"", submitted);
        var result = submitted[submitted.IndexOf("data-check=\"no-superlatives\"", StringComparison.Ordinal)..];
        Assert.StartsWith("data-check=\"no-superlatives\" data-status=\"fail\"", result);
        Assert.Contains("/headline", result);
        Assert.Contains("best in class", result);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_rejection_needs_a_reason_which_the_view_then_shows(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var set = await AwaitingReviewAsync(host, ownerId, "product");
        await host.SignInAsync(AdminHost.Owner);

        var blank = await (await ActAsync(host, set.Id, "reject", ("Reject.Reason", "  "))).Content.ReadAsStringAsync();
        Assert.Contains("data-testid=\"review-refusal\"", blank);
        Assert.Contains("data-state=\"awaiting-review\"", blank);

        var rejected = await (await ActAsync(host, set.Id, "reject", ("Reject.Reason", "The prices are from last year."))).Content.ReadAsStringAsync();
        Assert.Contains("data-state=\"rejected\"", rejected);
        Assert.Contains("The prices are from last year.", rejected);
        Assert.DoesNotContain("data-action=", rejected);
        Assert.Equal(ChangeSetState.Rejected, (await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async sets => (await sets.GetAsync(set.Id)).Value)).State);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Revert_opens_the_set_that_undoes_a_published_one_in_one_click_and_lands_on_it(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var set = await AwaitingReviewAsync(host, ownerId, "product");
        await host.SignInAsync(AdminHost.Owner);
        await ActAsync(host, set.Id, "approve");
        await ActAsync(host, set.Id, "publish");

        var reverted = await ActAsync(host, set.Id, "revert");

        Assert.Equal(HttpStatusCode.Redirect, reverted.StatusCode);
        var location = host.LocationOf(reverted)!;
        var revertId = Guid.Parse(location["/sets/".Length..]);
        var page = await host.Client.GetStringAsync(location);
        Assert.Contains("data-state=\"open\"", page);
        Assert.Contains($"href=\"sets/{set.Id}\"", page);
        Assert.Contains($"Revert change set {set.Id}: Add the product type.", page);
        var revert = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async sets => (await sets.GetAsync(revertId)).Value);
        Assert.Equal(set.Id, revert.RevertsChangeSetId);

        // Back and click again lands on the revert already in progress.
        var again = await ActAsync(host, set.Id, "revert");
        Assert.Equal(location, host.LocationOf(again));
        Assert.Equal(1, await QueryAsync(host, context => context.ChangeSets.CountAsync(row => row.RevertsChangeSetId == set.Id)));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_action_posted_for_a_state_that_does_not_offer_it_is_refused_and_changes_nothing(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var open = await OpenAsync(host, ownerId, "product");
        var running = await OpenAsync(host, ownerId, "category", ChangeSetState.ChecksRunning);
        var failed = await OpenAsync(host, ownerId, "brand", ChangeSetState.ChecksRunning, ChangeSetState.ChecksFailed);
        var waiting = await AwaitingReviewAsync(host, ownerId, "region");
        var approved = await OpenAsync(host, ownerId, "store", ChangeSetState.ChecksRunning, ChangeSetState.AwaitingReview, ChangeSetState.Approved);
        var published = await OpenAsync(host, ownerId, "tag", ChangeSetState.ChecksRunning, ChangeSetState.AwaitingReview, ChangeSetState.Approved);
        await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async sets => (await sets.PublishAsync(published.Id)).Value);
        await host.SignInAsync(AdminHost.Owner);

        // Each post carries the set's current Seen, as someone could read it from the audit page.
        (ChangeSet Set, string Action)[] refused =
        [
            (running, "approve"), (failed, "approve"), (open, "approve"), (published, "approve"),
            (waiting, "publish"), (approved, "revert"), (failed, "submit"), (open, "reject"),
        ];
        foreach (var (set, action) in refused)
        {
            var before = await StateAsync(host, ownerId, set.Id);
            var form = new Dictionary<string, string> { [$"{char.ToUpperInvariant(action[0])}{action[1..]}.Seen"] = await LastMoveAsync(host, set.Id) };
            if (action == "reject")
            {
                form["Reject.Reason"] = "Not now.";
            }

            var page = await (await host.SubmitAsync($"/sets/{set.Id}", action, form)).Content.ReadAsStringAsync();

            Assert.True(page.Contains("data-testid=\"review-refusal\"", StringComparison.Ordinal), $"{action} on a {before} set was not refused.");
            Assert.Equal(before, await StateAsync(host, ownerId, set.Id));
        }

        Assert.Equal(0, await QueryAsync(host, context => context.ChangeSets.CountAsync(row => row.RevertsChangeSetId != null)));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_action_on_a_set_that_moved_on_since_the_page_was_shown_is_refused(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var set = await AwaitingReviewAsync(host, ownerId, "product");
        await host.SignInAsync(AdminHost.Owner);
        var seen = Seen(await host.Client.GetStringAsync($"/sets/{set.Id}"), "Approve");

        // Another reviewer approves it in the meantime.
        await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async sets => (await sets.TransitionAsync(set.Id, ChangeSetState.Approved)).Value);

        var stale = await host.SubmitAsync($"/sets/{set.Id}", "approve", new Dictionary<string, string> { ["Approve.Seen"] = seen });
        var page = await stale.Content.ReadAsStringAsync();

        Assert.Contains("data-testid=\"review-refusal\"", page);
        Assert.Contains("data-state=\"approved\"", page);
        Assert.Single(await MovesAsync(host, set.Id), row => row.Operation == "change_set.approved");
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_post_without_the_anti_forgery_token_changes_nothing_and_an_anonymous_one_goes_to_sign_in(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var set = await AwaitingReviewAsync(host, ownerId, "product");

        string[] actions = ["submit", "approve", "publish", "reject", "revert"];
        foreach (var action in actions)
        {
            var anonymous = await host.Client.PostAsync($"/sets/{set.Id}", Form(("_handler", action)));
            Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
            Assert.StartsWith("/sign-in?", host.LocationOf(anonymous));
        }

        await host.SignInAsync(AdminHost.Owner);
        var seen = await LastMoveAsync(host, set.Id);
        var events = (await MovesAsync(host, set.Id)).Count;
        foreach (var action in actions)
        {
            var name = $"{char.ToUpperInvariant(action[0])}{action[1..]}";
            var forged = await host.Client.PostAsync($"/sets/{set.Id}", Form(("_handler", action), ($"{name}.Seen", seen), ("Reject.Reason", "Forged.")));
            Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        }

        Assert.Equal(events, (await MovesAsync(host, set.Id)).Count);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Only_the_actions_the_state_allows_are_offered(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var waiting = await AwaitingReviewAsync(host, ownerId, "product");
        var failed = await OpenAsync(host, ownerId, "category", ChangeSetState.ChecksRunning, ChangeSetState.ChecksFailed);
        var running = await OpenAsync(host, ownerId, "brand", ChangeSetState.ChecksRunning);
        await host.SignInAsync(AdminHost.Owner);

        Assert.Equal(["approve", "reject"], Actions(await host.Client.GetStringAsync($"/sets/{waiting.Id}")));
        Assert.Equal(["reject"], Actions(await host.Client.GetStringAsync($"/sets/{failed.Id}")));
        Assert.Empty(Actions(await host.Client.GetStringAsync($"/sets/{running.Id}")));
    }

    private static Task<HttpResponseMessage> ActAsync(AdminHost host, Guid id, string action, params (string Name, string Value)[] fields)
    {
        return ActAsync(host, id, action, string.Empty, fields);
    }

    private static async Task<HttpResponseMessage> ActAsync(AdminHost host, Guid id, string action, string query, params (string Name, string Value)[] fields)
    {
        var path = $"/sets/{id}{query}";
        var form = fields.ToDictionary(field => field.Name, field => field.Value);
        form[$"{char.ToUpperInvariant(action[0])}{action[1..]}.Seen"] = Seen(await host.Client.GetStringAsync(path), action);
        return await host.SubmitAsync(path, action, form);
    }

    private static string Seen(string page, string action)
    {
        var name = $"{char.ToUpperInvariant(action[0])}{action[1..]}.Seen";
        var match = Regex.Match(page, $"name=\"{Regex.Escape(name)}\" value=\"([0-9a-f]+)\"");
        Assert.True(match.Success, $"The page has no {name} field.");
        return match.Groups[1].Value;
    }

    private static List<string> Actions(string page)
    {
        return [.. ActionAttribute().Matches(page).Select(match => match.Groups[1].Value)];
    }

    private static FormUrlEncodedContent Form(params (string Name, string Value)[] fields)
    {
        return new FormUrlEncodedContent(fields.ToDictionary(field => field.Name, field => field.Value));
    }

    private static Task<ChangeSet> AwaitingReviewAsync(AdminHost host, Guid userId, string slug)
    {
        return OpenAsync(host, userId, slug, ChangeSetState.ChecksRunning, ChangeSetState.AwaitingReview);
    }

    private static async Task<ChangeSet> OpenAsync(AdminHost host, Guid userId, string slug, params ChangeSetState[] moves)
    {
        var type = await host.AsUserAsync<IContentTypeDirectory, ContentType>(userId, async directory =>
            (await directory.CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition([new FieldDefinition("title", "text")])), TestChangeSetTargets.ContentModel)).Value);

        return await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(userId, async directory =>
        {
            var set = (await directory.OpenAsync(new NewChangeSet($"Add the {slug} type.", []))).Value;
            set = (await directory.AddOperationAsync(set.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, 1).Value)).Value;
            foreach (var move in moves)
            {
                set = (await directory.TransitionAsync(set.Id, move)).Value;
            }

            return set;
        });
    }

    private static Task<ChangeSetState> StateAsync(AdminHost host, Guid userId, Guid id)
    {
        return host.AsUserAsync<IChangeSetDirectory, ChangeSetState>(userId, async sets => (await sets.GetAsync(id)).Value.State);
    }

    // The Seen of a set as the page computes it: the id of its last move.
    private static async Task<string> LastMoveAsync(AdminHost host, Guid id)
    {
        return (await MovesAsync(host, id)).Last(row => ChangeSetMoves.Operations.Contains(row.Operation)).Id.ToString("N");
    }

    private static async Task<T> QueryAsync<T>(AdminHost host, Func<FluentCmsDbContext, Task<T>> query)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());
    }

    private static async Task<List<Infrastructure.Audit.AuditEventRow>> MovesAsync(AdminHost host, Guid id)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().AuditEvents
            .AsNoTracking()
            .Where(row => row.SubjectType == "change_set" && row.SubjectId == id)
            .ToListAsync();
        return [.. rows.OrderBy(row => row.Timestamp)];
    }

    [GeneratedRegex("data-action=\"([a-z]+)\"")]
    private static partial Regex ActionAttribute();
}
