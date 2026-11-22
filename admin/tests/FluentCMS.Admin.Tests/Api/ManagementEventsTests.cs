using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Api;

// The events of a route that answers text/event-stream (A-38), over the admin's one pipeline: its headers, the events read as
// the server wrote them, and a refusal before the first event, which is not sent again, a 429 included.
public sealed class ManagementEventsTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private static ApiRequest Action => new(HttpMethod.Post, "ai/actions/rewrite") { Body = new JsonObject { ["text"] = "Ships." } };

    [Fact]
    public async Task TheEventsAreReadInOrderWithTheSessionsHeaders()
    {
        using var harness = new ApiHarness();
        harness.Session.Site = "blog";
        harness.Server.Answer(HttpStatusCode.OK, ": a comment\n\nevent: delta\ndata: {\"text\":\"Ships \"}\n\nevent: delta\ndata: {\"text\":\"sail.\"}\n\nevent:done\ndata: {\"suggestion\":\"Ships sail.\"}\ndata: {}\n\nevent: delta\ndata: {\"text\":\"cut off\"}", "text/event-stream");

        List<ApiEvent> events = [];
        await foreach (var sent in harness.Events.ReadAsync(Action, Token))
        {
            events.Add(sent);
        }

        events.Select(sent => sent.Name).ShouldBe(["delta", "delta", "done"]);
        events[1].Data.ShouldBe("""{"text":"sail."}""");
        events[2].Data.ShouldBe("{\"suggestion\":\"Ships sail.\"}\n{}");
        var sentRequest = harness.Server.Requests.ShouldHaveSingleItem();
        sentRequest.Header("Accept").ShouldBe("text/event-stream");
        sentRequest.Header("X-Cms-Site").ShouldBe("blog");
        sentRequest.Header("Authorization").ShouldBe("Bearer token-1");
        sentRequest.Body.ShouldBe("""{"text":"Ships."}""");
    }

    // A daily budget is not lifted by waiting, so the caller shows the problem: it is sent once and the shell tells nothing.
    [Fact]
    public async Task ARefusalBeforeTheFirstEventIsAProblemAndA429IsNotSentAgain()
    {
        using var harness = new ApiHarness();
        harness.Server.Problem(
            HttpStatusCode.TooManyRequests,
            """{"status":429,"code":"FCMS1693","title":"AI token budget used","detail":"The site has used its daily budget of 1000 AI tokens."}""",
            response => response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2)));

        var thrown = await Should.ThrowAsync<ApiProblemException>(async () =>
        {
            await foreach (var _ in harness.Events.ReadAsync(Action, Token))
            {
            }
        });

        (thrown.Problem.Status, thrown.Problem.Code, thrown.Problem.Detail).ShouldBe((429, "FCMS1693", "The site has used its daily budget of 1000 AI tokens."));
        harness.Server.Requests.Count.ShouldBe(1);
        harness.Time.Waits.ShouldBeEmpty();
        harness.Session.Notices.ShouldBeEmpty();
    }

    [Fact]
    public async Task ASuccessThatIsNoEventStreamIsRefused()
    {
        using var harness = new ApiHarness();
        harness.Server.Answer(HttpStatusCode.OK, """{"suggestion":"Ships sail."}""");

        await Should.ThrowAsync<JsonException>(async () =>
        {
            await foreach (var _ in harness.Events.ReadAsync(Action, Token))
            {
            }
        });
    }
}
