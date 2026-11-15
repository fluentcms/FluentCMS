using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentCMS.Content;
using FluentCMS.Hooks;
using FluentCMS.Plugins;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluentCms_Plugin1.Tests;

// The test host discovers the plugin, as a host that references its package does, applies its migrations to an in-memory
// SQLite database and serves its endpoints in process.
public sealed class StarterPluginTests : IAsyncDisposable
{
    private const string Notes = "/api/management/v1/starter-plugin/notes";
    private const string Settings = "/api/management/v1/settings/starter-plugin";

    private readonly FluentCmsTestHost _host = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ThePluginStartsWithItsTable()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<StarterPlugin>>();

        Assert.Empty(await database.Database.GetPendingMigrationsAsync(Token));
        Assert.Equal(0, await database.Set<StarterNote>().CountAsync(Token));
    }

    [Fact]
    public async Task TheHookIsRegisteredForEveryContentType()
    {
        await using var scope = _host.Services.CreateAsyncScope();

        Assert.Contains(scope.ServiceProvider.GetServices<IAfterPublishHook<ContentDocument>>(), hook => hook is StarterHook);
    }

    [Fact]
    public async Task AKeyWithThePermissionAddsAndReadsNotes()
    {
        using var client = await ClientAsync(StarterPermissions.NotesManage);

        using var created = await client.PostAsJsonAsync(Notes, new StarterNoteRequest("Hello"), Token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var notes = await client.GetFromJsonAsync<List<StarterNoteResponse>>(Notes, Token);
        Assert.Equal("Hello", Assert.Single(notes!).Text);
    }

    [Fact]
    public async Task AKeyWithoutThePermissionIsRefused()
    {
        using var client = await ClientAsync("content.article.read");

        using var response = await client.GetAsync(new Uri(Notes, UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TheSettingsOfTheSiteLimitTheNotes()
    {
        using var client = await ClientAsync(StarterPermissions.NotesManage, "settings.starter-plugin.manage");
        await SetMaxNotesAsync(client, 1);

        using var first = await client.PostAsJsonAsync(Notes, new StarterNoteRequest("One"), Token);
        using var second = await client.PostAsJsonAsync(Notes, new StarterNoteRequest("Two"), Token);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task PostsSentTogetherAddNoMoreNotesThanTheSettingsAllow()
    {
        using var client = await ClientAsync(StarterPermissions.NotesManage, "settings.starter-plugin.manage");
        await SetMaxNotesAsync(client, 3);

        var responses = await Task.WhenAll(Enumerable.Range(1, 10).Select(number => client.PostAsJsonAsync(Notes, new StarterNoteRequest($"Note {number}"), Token)));
        var statuses = responses.Select(response => response.StatusCode).ToList();
        Array.ForEach(responses, response => response.Dispose());

        // A post that waits too long for the lock answers 503 and adds nothing, so what holds is the limit: no more notes
        // than MaxNotes, and one for each post answered 201.
        HttpStatusCode[] answers = [HttpStatusCode.Created, HttpStatusCode.Conflict, HttpStatusCode.ServiceUnavailable];
        Assert.All(statuses, status => Assert.Contains(status, answers));
        var created = statuses.Count(status => status == HttpStatusCode.Created);
        Assert.InRange(created, 1, 3);
        var notes = await client.GetFromJsonAsync<List<StarterNoteResponse>>(Notes, Token);
        Assert.Equal(created, notes!.Count);
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    // A write names the version of the settings it read, 0 for a site that has none of its own yet.
    private static async Task SetMaxNotesAsync(HttpClient client, int max)
    {
        using var save = new HttpRequestMessage(HttpMethod.Put, new Uri(Settings, UriKind.Relative)) { Content = JsonContent.Create(new StarterSettings { MaxNotes = max }) };
        save.Headers.IfMatch.Add(new EntityTagHeaderValue("\"0\""));
        using var saved = await client.SendAsync(save, Token);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    // A Management key of a new role with these permissions, as an administrator would make one.
    private async Task<HttpClient> ClientAsync(params string[] permissions)
    {
        var role = "role-" + Guid.NewGuid().ToString("N");
        await _host.CreateRoleAsync(role, permissions, Token);
        return _host.AsKey(await _host.CreateApiKeyAsync(ApiKeyClass.Management, role, cancellationToken: Token));
    }
}
