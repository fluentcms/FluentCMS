using System.Net;
using System.Text.RegularExpressions;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Schema;

namespace FluentCMS.IntegrationTests.Desk;

public sealed partial class DeskScreenTests
{
    [Fact]
    public async Task An_anonymous_request_for_the_desk_is_sent_to_sign_in()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();

        var response = await host.Client.GetAsync("/?queue=checks-failed");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sign-in?ReturnUrl=%2F%3Fqueue%3Dchecks-failed", host.LocationOf(response));
    }

    [Fact]
    public async Task Signing_in_lands_on_the_desk_with_the_queues_and_their_counts()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var waiting = await AwaitingReviewAsync(host, ownerId, "product");
        await AwaitingReviewAsync(host, ownerId, "category");

        var signedIn = await host.SignInAsync(AdminHost.Owner);
        Assert.Equal("/", host.LocationOf(signedIn));
        var desk = await host.Client.GetStringAsync("/");

        Assert.Contains("data-testid=\"desk-queues\"", desk);
        Assert.Equal(
            [("awaiting-me", "2"), ("awaiting-anyone", "2"), ("checks-failed", "0"), ("stale-facts", "0"), ("recently-published", "0"), ("reverted", "0")],
            QueueCounts(desk));
        Assert.Contains("data-current-queue=\"awaiting-me\"", desk);
        Assert.Contains($"data-change-set=\"{waiting:N}\"", desk);
        Assert.Contains("Add the product type.", desk);
        Assert.Contains("data-testid=\"nav-desk\"", desk);

        var empty = await host.Client.GetStringAsync("/?queue=reverted");
        Assert.Contains("data-current-queue=\"reverted\"", empty);
        Assert.Contains("data-testid=\"desk-empty\"", empty);
        Assert.DoesNotContain("data-change-set=", empty);
    }

    [Fact]
    public async Task The_desk_renders_right_to_left()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var desk = await host.Client.GetStringAsync("/?dir=rtl");

        Assert.Contains("<html lang=\"en\" dir=\"rtl\">", desk);
        Assert.Contains("data-testid=\"desk-queues\"", desk);
    }

    private static async Task<Guid> AwaitingReviewAsync(AdminHost host, Guid userId, string slug)
    {
        var type = await host.AsUserAsync<IContentTypeDirectory, ContentType>(userId, async directory =>
            (await directory.CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition([new FieldDefinition("title", "text")])), TestChangeSetTargets.ContentModel)).Value);

        return await host.AsUserAsync<IChangeSetDirectory, Guid>(userId, async directory =>
        {
            var set = (await directory.OpenAsync(new NewChangeSet($"Add the {slug} type.", []))).Value;
            Assert.True((await directory.AddOperationAsync(set.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, 1).Value)).IsSuccess);
            Assert.True((await directory.TransitionAsync(set.Id, ChangeSetState.ChecksRunning)).IsSuccess);
            Assert.True((await directory.TransitionAsync(set.Id, ChangeSetState.AwaitingReview)).IsSuccess);
            return set.Id;
        });
    }

    private static List<(string Queue, string Count)> QueueCounts(string html)
    {
        return [.. QueueLink().Matches(html).Select(match => (match.Groups[1].Value, match.Groups[2].Value))];
    }

    [GeneratedRegex("data-queue=\"([a-z-]+)\"[^>]*>[^<]*<span[^>]*data-count=\"(\\d+)\"")]
    private static partial Regex QueueLink();
}
