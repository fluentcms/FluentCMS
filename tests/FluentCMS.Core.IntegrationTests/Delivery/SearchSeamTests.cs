using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Providers;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// The search seam with an index in the slot, as the search plugin would fill it, on a host per test.
public sealed class SearchSeamTests : IAsyncLifetime
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly FluentCmsTestHost _host = new FluentCmsTestHost()
        .WithContentTypes<Note, Topic>()
        .Configure(cms =>
        {
            cms.DeliveryCache.OutputCache = false;
            cms.Providers.Use<ISearchIndex, FakeSearchIndex>();
        });

    private HttpClient _delivery = null!;
    private HttpClient _management = null!;
    private Note _first = null!;
    private Note _second = null!;
    private Note _third = null!;

    private FakeSearchIndex Index => (FakeSearchIndex)_host.Services.GetRequiredService<ISearchIndex>();

    public async ValueTask InitializeAsync()
    {
        var notes = _host.As(await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token)).Content<Note>();
        _first = await PublishedAsync(notes, "First");
        _second = await PublishedAsync(notes, "Second");
        _third = await PublishedAsync(notes, "Third");

        _delivery = _host.AsKey(await _host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.EditorName, cancellationToken: Token));
        _management = _host.AsKey(await _host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));
    }

    public async ValueTask DisposeAsync()
    {
        _delivery.Dispose();
        _management.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task DeliveryNarrowsAListToWhatTheIndexFindsAndKeepsItsFilterAndSort()
    {
        Index.Hits["climate"] = [_third.Id, _second.Id, _first.Id];

        var (status, body) = await GetAsync(_delivery, "/api/delivery/v1/note?q=climate&filter[title][ne]=Second&sort=-title&withTotal=true");

        status.ShouldBe(HttpStatusCode.OK);
        Titles(body).ShouldBe(["Third", "First"]);
        body["total"]!.GetValue<long>().ShouldBe(2);
        Index.Requests.ShouldHaveSingleItem().ShouldBe(new SearchRequest("note", "climate", 1000) { Site = new SiteId(DefaultSite.Id), Status = QueryStatus.Published });
    }

    [Fact]
    public async Task TheManagementApiSearchesTheDraftsItLists()
    {
        Index.Hits["ocean"] = [_second.Id];

        var (status, body) = await GetAsync(_management, "/api/management/v1/content/note?q=ocean");

        status.ShouldBe(HttpStatusCode.OK);
        Titles(body).ShouldBe(["Second"]);
        Index.Requests.ShouldHaveSingleItem().Status.ShouldBe(QueryStatus.Draft);
    }

    // An index that returns as many ids as it is asked for may have left matches out: both APIs say so in the list, and say
    // nothing of it otherwise.
    [Theory]
    [InlineData("/api/delivery/v1/note")]
    [InlineData("/api/management/v1/content/note")]
    public async Task AListSaysWhenItsSearchReachedTheMostMatchesOfTheIndex(string path)
    {
        var client = path.Contains("/delivery/", StringComparison.Ordinal) ? _delivery : _management;
        Index.Hits["everything"] = [_first.Id, _second.Id, _third.Id];
        Index.Full.Add("everything");
        Index.Hits["few"] = [_first.Id];

        var (status, cut) = await GetAsync(client, $"{path}?q=everything&withTotal=true");
        var (_, whole) = await GetAsync(client, $"{path}?q=few");
        var (_, unsearched) = await GetAsync(client, path);

        status.ShouldBe(HttpStatusCode.OK);
        Titles(cut).Order(StringComparer.Ordinal).ShouldBe(["First", "Second", "Third"]);
        cut["total"]!.GetValue<long>().ShouldBe(3);
        cut["searchTruncated"]!.GetValue<bool>().ShouldBeTrue();
        whole.AsObject().ContainsKey("searchTruncated").ShouldBeFalse();
        unsearched.AsObject().ContainsKey("searchTruncated").ShouldBeFalse();
    }

    // A page read by cursor, a list in every locale and the typed service say it as the first page does.
    [Fact]
    public async Task EveryReadOfACutSearchSaysSo()
    {
        Index.Hits["everything"] = [_first.Id, _second.Id, _third.Id];
        Index.Full.Add("everything");
        const string Path = "/api/management/v1/content/note?q=everything&sort=title&pageSize=1";

        var (_, first) = await GetAsync(_management, Path);
        var (status, next) = await GetAsync(_management, $"{Path}&cursor={Uri.EscapeDataString(first["nextCursor"]!.GetValue<string>())}");
        var (everyStatus, every) = await GetAsync(_management, $"{Path}&locale=all");
        var typed = await _host.As(await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token)).Content<Note>().QueryAsync(new ContentQuery { Search = "everything" }, Token);

        status.ShouldBe(HttpStatusCode.OK);
        Titles(next).ShouldBe(["Second"]);
        next["searchTruncated"]!.GetValue<bool>().ShouldBeTrue();
        everyStatus.ShouldBe(HttpStatusCode.OK);
        every["searchTruncated"]!.GetValue<bool>().ShouldBeTrue();
        typed.Items.Count.ShouldBe(3);
        typed.SearchTruncated.ShouldBeTrue();
    }

    // The index is asked for the drafts out of the trash, so their count says nothing of the trash.
    [Fact]
    public async Task AReadOfTheTrashNeverSaysItsSearchWasCut()
    {
        Index.Hits["everything"] = [_first.Id, _second.Id, _third.Id];
        Index.Full.Add("everything");
        await _host.As(await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token)).Content<Note>().DeleteAsync(_first.Id, Token);

        var (status, trash) = await GetAsync(_management, "/api/management/v1/content/note/trash?q=everything");

        status.ShouldBe(HttpStatusCode.OK);
        Titles(trash).ShouldBe(["First"]);
        trash.AsObject().ContainsKey("searchTruncated").ShouldBeFalse();
    }

    [Theory]
    [InlineData("/api/delivery/v1/note/{0}?q=x")]
    [InlineData("/api/management/v1/content/note/{0}?q=x")]
    public async Task ASingleEntryRefusesASearch(string path)
    {
        var client = path.Contains("/delivery/", StringComparison.Ordinal) ? _delivery : _management;

        var (status, body) = await GetAsync(client, path.Replace("{0}", _first.Id.ToString(), StringComparison.Ordinal));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body["code"]!.GetValue<string>().ShouldBe("FCMS2201");
        body["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("q");
        Index.Requests.ShouldBeEmpty();
    }

    private static async Task<Note> PublishedAsync(IContentService<Note> notes, string title)
    {
        var created = await notes.CreateAsync(new Note { Title = title, Slug = title.ToLowerInvariant() }, Token);
        await notes.PublishAsync(created.Id, cancellationToken: Token);
        return created;
    }

    private static List<string> Titles(JsonNode body) => [.. body["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>())];

    private static async Task<(HttpStatusCode Status, JsonNode Body)> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Token);
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!);
    }

    // Finds what a test gave it for a text, and keeps what it was asked. A text in Full finds as many ids as the limit, the
    // test's first and ids of no entry after them.
    public sealed class FakeSearchIndex : ISearchIndex
    {
        public ConcurrentDictionary<string, Guid[]> Hits { get; } = new(StringComparer.Ordinal);

        public ConcurrentBag<string> Full { get; } = [];

        public ConcurrentQueue<SearchRequest> Requests { get; } = new();

        public Task<IReadOnlyList<Guid>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request);
            var hits = Hits.GetValueOrDefault(request.Text, []);
            return Task.FromResult<IReadOnlyList<Guid>>(Full.Contains(request.Text)
                ? [.. hits, .. Enumerable.Range(0, request.Limit - hits.Length).Select(_ => Guid.CreateVersion7())]
                : hits);
        }
    }
}
