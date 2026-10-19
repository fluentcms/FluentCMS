using System.Net;
using System.Text.RegularExpressions;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Schema;

namespace FluentCMS.IntegrationTests.Desk;

public sealed class ChangeSetScreenTests
{
    [Fact]
    public async Task An_anonymous_request_for_a_set_is_sent_to_sign_in()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();
        var id = Guid.NewGuid();

        var response = await host.Client.GetAsync($"/sets/{id}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/sign-in?ReturnUrl=%2Fsets%2F", host.LocationOf(response));
    }

    [Fact]
    public async Task An_unknown_set_is_not_found()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var response = await host.Client.GetAsync($"/sets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("There is nothing at this address.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_set_shows_its_rationale_sources_history_and_first_items_and_the_desk_links_to_it()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var types = new List<ContentType>();
        for (var index = 0; index < 30; index++)
        {
            types.Add(await CreateTypeAsync(host, ownerId, $"type-{index}"));
        }

        var set = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async directory =>
        {
            var opened = (await directory.OpenAsync(new NewChangeSet(
                "Add thirty types.",
                ["https://example.com/spec", "javascript:alert(1)", "specs/types.md"]))).Value;
            foreach (var type in types)
            {
                Assert.True((await directory.AddOperationAsync(opened.Id, Create(type))).IsSuccess);
            }

            await directory.TransitionAsync(opened.Id, ChangeSetState.ChecksRunning);
            return (await directory.TransitionAsync(opened.Id, ChangeSetState.AwaitingReview)).Value;
        });
        await host.SignInAsync(AdminHost.Owner);

        Assert.Contains($"href=\"sets/{set.Id}\"", await host.Client.GetStringAsync("/"));
        var page = await host.Client.GetStringAsync($"/sets/{set.Id}");

        Assert.Contains("<h1 class=\"text-2xl font-semibold tracking-tight whitespace-pre-line\">Add thirty types.</h1>", page);
        Assert.Contains("data-state=\"awaiting-review\"", page);
        Assert.Contains("href=\"https://example.com/spec\" target=\"_blank\" rel=\"noopener noreferrer\"", page);
        Assert.DoesNotContain("href=\"javascript:", page);
        Assert.Contains("specs/types.md", page);
        Assert.Equal(
            ["change_set.opened", "change_set.submitted", "change_set.awaiting_review"],
            Regex.Matches(page[page.IndexOf("data-testid=\"change-set-history\"", StringComparison.Ordinal)..], "data-operation=\"([a-z_.]+)\"").Select(match => match.Groups[1].Value));

        // The island arrives with the page holding the first batch; the circuit adds the rest.
        Assert.Equal(25, Regex.Count(page, "data-slot=\"diff-view-item\""));
        Assert.Contains("aria-busy=\"true\"", page);
        Assert.Contains("<!--Blazor:", page);
    }

    [Fact]
    public async Task A_rejected_revert_set_shows_its_reason_and_links_to_the_set_it_reverts()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var type = await CreateTypeAsync(host, ownerId, "product");
        var published = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async directory =>
        {
            var opened = (await directory.OpenAsync(new NewChangeSet("Add products.", []))).Value;
            await directory.AddOperationAsync(opened.Id, Create(type));
            await directory.TransitionAsync(opened.Id, ChangeSetState.ChecksRunning);
            await directory.TransitionAsync(opened.Id, ChangeSetState.Approved);
            return (await directory.PublishAsync(opened.Id)).Value;
        });
        var revert = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async directory =>
        {
            var opened = (await directory.RevertAsync(published.Id, "Products were added too early.")).Value;
            await directory.TransitionAsync(opened.Id, ChangeSetState.ChecksRunning);
            await directory.TransitionAsync(opened.Id, ChangeSetState.AwaitingReview);
            return (await directory.RejectAsync(opened.Id, "Keep the products; the launch moved up.")).Value;
        });
        await host.SignInAsync(AdminHost.Owner);

        var page = await host.Client.GetStringAsync($"/sets/{revert.Id}");

        Assert.Contains("data-state=\"rejected\"", page);
        Assert.Contains("Keep the products; the launch moved up.", page);
        Assert.Contains($"href=\"sets/{published.Id}\"", page);
    }

    private static Task<ContentType> CreateTypeAsync(AdminHost host, Guid userId, string slug)
    {
        return host.AsUserAsync<IContentTypeDirectory, ContentType>(userId, async directory =>
            (await directory.CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition([new FieldDefinition("title", "text")])), TestChangeSetTargets.ContentModel)).Value);
    }

    private static ChangeOperation Create(ContentType type)
    {
        return ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, 1).Value;
    }
}
