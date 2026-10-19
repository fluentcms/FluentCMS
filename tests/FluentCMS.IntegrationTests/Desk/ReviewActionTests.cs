using System.Net;
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
    [Fact]
    public async Task A_reviewer_approves_and_then_publishes_a_set_and_each_step_names_them()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var set = await AwaitingReviewAsync(host, ownerId, "product");
        await host.SignInAsync(AdminHost.Owner);

        var approved = await ActAsync(host, set.Id, "approve");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
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

    [Fact]
    public async Task Reviewing_from_a_queue_moves_on_to_its_next_set_and_back_to_the_queue_once_it_is_empty()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
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

    [Fact]
    public async Task A_rejection_needs_a_reason_which_the_view_then_shows()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
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

    [Fact]
    public async Task Revert_opens_the_set_that_undoes_a_published_one_in_one_click_and_lands_on_it()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
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
    }

    [Fact]
    public async Task An_action_on_a_set_that_moved_on_since_the_page_was_shown_is_refused()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
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

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_changes_nothing_and_an_anonymous_one_goes_to_sign_in()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var set = await AwaitingReviewAsync(host, ownerId, "product");

        var anonymous = await host.Client.PostAsync($"/sets/{set.Id}", Form(("_handler", "approve")));
        await host.SignInAsync(AdminHost.Owner);
        var seen = Seen(await host.Client.GetStringAsync($"/sets/{set.Id}"), "Approve");
        var forged = await host.Client.PostAsync($"/sets/{set.Id}", Form(("_handler", "approve"), ("Approve.Seen", seen)));

        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/sign-in?", host.LocationOf(anonymous));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.DoesNotContain(await MovesAsync(host, set.Id), row => row.Operation == "change_set.approved");
    }

    [Fact]
    public async Task Only_the_actions_the_state_allows_are_offered()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
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
