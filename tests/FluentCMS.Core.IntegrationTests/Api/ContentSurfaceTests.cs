using System.Net;
using System.Net.Http.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.IntegrationTests.Testing;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Api;

// The SDK's seam for a surface over content that is not REST: it answers with the limits, the JSON and the access
// answers of the REST APIs.
public sealed class ContentSurfaceTests : IAsyncDisposable
{
    private readonly FluentCmsTestHost _host = new FluentCmsTestHost().WithContentTypes<Note>();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task TheLimitsAreThoseOfTheQueryOptions()
    {
        var limits = await _host.Anonymous.RunAsync<IContentSurface, (int, int, int)>(surface =>
            Task.FromResult((surface.DefaultPageSize, surface.MaxPageSize, surface.MaxPopulateDepth)));

        limits.ShouldBe((20, 100, 3));
    }

    [Fact]
    public async Task TheReadableFieldsAreWhatThePrincipalsGrantsAllow()
    {
        string[] fields = ["title"];
        using var admin = _host.AsKey(await _host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));
        using var role = await admin.PostAsJsonAsync(
            new Uri("/api/management/v1/roles", UriKind.Relative),
            new { name = "title-reader", grants = new[] { new { permission = "content.note.read", constraints = new { fields } } } },
            Token);
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync(Token));
        var limited = _host.As(await _host.CreateApiKeyAsync(ApiKeyClass.Delivery, "title-reader", cancellationToken: Token));
        var everything = _host.As(await _host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));

        var ofLimited = await ReadableAsync(limited, "note");
        var ofEverything = await ReadableAsync(everything, "note");

        ofLimited.ShouldNotBeNull();
        ofLimited.ShouldContain("title");
        ofLimited.ShouldNotContain("views");
        ofEverything.ShouldNotBeNull();
        string[] every = ["id", "status", "version", "createdAt", "title", "views"];
        every.ShouldBeSubsetOf(ofEverything);

        // A surface such as GraphQL does not answer the Management shape, so the pending schedules are no fields of it.
        ofEverything.ShouldNotContain("scheduledPublishAt");
        ofEverything.ShouldNotContain("scheduledUnpublishAt");

        // A type the principal may not read and one that does not exist are the same answer.
        (await ReadableAsync(_host.Anonymous, "note")).ShouldBeNull();
        (await ReadableAsync(everything, "nothing")).ShouldBeNull();

        static Task<IReadOnlySet<string>?> ReadableAsync(TestActor actor, string type) =>
            actor.RunAsync<IContentSurface, IReadOnlySet<string>?>(async surface => await surface.ReadableFieldsAsync(type, null, Token));
    }

    [Fact]
    public async Task AnEntryIsWrittenAsRestWritesIt()
    {
        var admin = _host.As(await _host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var created = await admin.Content<Note>().CreateAsync(new Note { Title = "Shown", Views = 3 }, Token);

        var entry = await admin.Content().GetAsync("note", created.Id, new GetOptions { Status = QueryStatus.Draft }, Token);
        var json = await admin.RunAsync<IContentSurface, System.Text.Json.Nodes.JsonObject>(surface => Task.FromResult(surface.ToJson(entry!)));

        json["id"]!.GetValue<string>().ShouldBe(created.Id.ToString("D"));
        json["status"]!.GetValue<string>().ShouldBe("draft");
        json["title"]!.GetValue<string>().ShouldBe("Shown");
        json["views"]!.GetValue<long>().ShouldBe(3);
    }
}
