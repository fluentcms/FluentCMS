using System.Net;
using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;

namespace FluentCMS.IntegrationTests.Desk;

// The layout's counts load while the page loads its own data. On PostgreSQL two queries on one DbContext collide, which SQLite's
// synchronous calls hide, so every signed-in page is loaded again and again on both providers.
public sealed class SignedInPageTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Every_signed_in_page_loads_every_time(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var (set, entry) = await AwaitingReviewAsync(host, ownerId);
        await host.SignInAsync(AdminHost.Owner);

        string[] paths =
        [
            "/", "/?queue=stale-facts", $"/sets/{set}", "/security", "/audit", "/users", "/agents", "/webhooks", "/media",
            "/content", "/content/product", "/content/product/new", $"/content/product/{entry}",
        ];
        var failures = new List<string>();
        for (var round = 0; round < 5; round++)
        {
            foreach (var path in paths)
            {
                var response = await host.Client.GetAsync(path);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    failures.Add($"{path}: {(int)response.StatusCode}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static async Task<(Guid Set, Guid Entry)> AwaitingReviewAsync(AdminHost host, Guid ownerId)
    {
        await host.AsUserAsync<IContentTypeDirectory, ContentType>(ownerId, async types => (await types.CreateAsync(
            new NewContentType("Product", "product", new ContentTypeDefinition([new FieldDefinition("title", "text")])),
            TestChangeSetTargets.ContentModel)).Value);
        var entry = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries =>
            (await entries.CreateAsync(new NewEntry("product", JsonSerializer.Deserialize<JsonElement>("""{"title": "Drill"}""")))).Value);
        var set = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async sets =>
        {
            var opened = (await sets.OpenAsync(new NewChangeSet("Add the drill.", []))).Value;
            await sets.AddOperationAsync(opened.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Entry, entry.Id, null, 1).Value);
            await sets.TransitionAsync(opened.Id, ChangeSetState.ChecksRunning);
            return (await sets.TransitionAsync(opened.Id, ChangeSetState.AwaitingReview)).Value;
        });
        return (set.Id, entry.Id);
    }
}
