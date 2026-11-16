using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Api;

public sealed class RateLimitTests
{
    private const string Me = """{"id":"01a1e1f8-ff43-7810-88a0-6e0124e0827a","kind":"adminUser","name":"Ada"}""";
    private const string TooMany = """{"status":429,"code":"FCMS2901","title":"Too many requests"}""";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A429WaitsItsRetryAfterAndTriesOnceMore()
    {
        using var harness = new ApiHarness();
        harness.Server.Problem(HttpStatusCode.TooManyRequests, TooMany, RetryAfter(7)).Answer(HttpStatusCode.OK, Me);

        var me = await harness.Client.GetMeAsync(Token);

        me.Name.ShouldBe("Ada");
        harness.Time.Waits.ShouldBe([TimeSpan.FromSeconds(7)]);
        harness.Server.Requests.Count.ShouldBe(2);
        harness.Session.Notices.ShouldBeEmpty();
    }

    [Fact]
    public async Task ASecond429IsShownAndSurfaces()
    {
        using var harness = new ApiHarness();
        harness.Server.Problem(HttpStatusCode.TooManyRequests, TooMany, RetryAfter(2)).Problem(HttpStatusCode.TooManyRequests, TooMany, RetryAfter(3));

        var thrown = await Should.ThrowAsync<ApiProblemException>(() => harness.Client.GetMeAsync(Token));

        thrown.Problem.Status.ShouldBe(429);
        harness.Server.Requests.Count.ShouldBe(2);
        harness.Time.Waits.ShouldBe([TimeSpan.FromSeconds(2)]);
        harness.Session.Notices.ShouldBe([TimeSpan.FromSeconds(3)]);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(3600)]
    [InlineData(5_000_000)]
    public async Task A429ThatAsksForMoreThanThirtySecondsIsShownAtOnce(int seconds)
    {
        using var harness = new ApiHarness();
        harness.Server.Problem(HttpStatusCode.TooManyRequests, TooMany, RetryAfter(seconds));

        var thrown = await Should.ThrowAsync<ApiProblemException>(() => harness.Client.GetMeAsync(Token));

        thrown.Problem.Status.ShouldBe(429);
        harness.Server.Requests.Count.ShouldBe(1);
        harness.Time.Waits.ShouldBeEmpty();
        harness.Session.Notices.ShouldBe([TimeSpan.FromSeconds(seconds)]);
    }

    [Fact]
    public async Task A429WaitsAThirtySecondRetryAfter()
    {
        using var harness = new ApiHarness();
        harness.Server.Problem(HttpStatusCode.TooManyRequests, TooMany, RetryAfter(30)).Answer(HttpStatusCode.OK, Me);

        await harness.Client.GetMeAsync(Token);

        harness.Time.Waits.ShouldBe([RateLimitHandler.MaxWait]);
    }

    [Fact]
    public async Task ARetryAfterDateIsWaitedUntil()
    {
        using var harness = new ApiHarness();
        harness.Server.Problem(HttpStatusCode.TooManyRequests, TooMany, response => response.Headers.RetryAfter = new RetryConditionHeaderValue(RecordingTime.Now.AddSeconds(5)))
            .Answer(HttpStatusCode.OK, Me);

        await harness.Client.GetMeAsync(Token);

        harness.Time.Waits.ShouldBe([TimeSpan.FromSeconds(5)]);
        harness.Session.Notices.ShouldBeEmpty();
    }

    [Fact]
    public async Task A429WithoutRetryAfterIsShownWithoutAWait()
    {
        using var harness = new ApiHarness();
        harness.Server.Problem(HttpStatusCode.TooManyRequests, TooMany);

        await Should.ThrowAsync<ApiProblemException>(() => harness.Client.GetMeAsync(Token));

        harness.Server.Requests.Count.ShouldBe(1);
        harness.Time.Waits.ShouldBeEmpty();
        harness.Session.Notices.ShouldBe([null]);
    }

    [Fact]
    public async Task TheRetryOfAWriteSendsItsBodyKeyAndHeadersAgain()
    {
        using var harness = new ApiHarness();
        harness.Session.Site = "blog";
        harness.Server.Problem(HttpStatusCode.TooManyRequests, TooMany, RetryAfter(1)).Answer(HttpStatusCode.NoContent);
        var request = new ApiRequest(HttpMethod.Post, "content/tag/bulk") { Body = new JsonObject { ["action"] = "publish" }, IdempotencyKey = "key-1" };

        await harness.Api.SendAsync(request, Token);

        harness.Server.Requests.Select(sent => sent.Body).ShouldBe(["""{"action":"publish"}""", """{"action":"publish"}"""]);
        harness.Server.Requests.Select(sent => sent.Header("Idempotency-Key")).ShouldBe(["key-1", "key-1"]);
        harness.Server.Requests.Select(sent => sent.Header("X-Cms-Site")).ShouldBe(["blog", "blog"]);
        harness.Server.Requests.Select(sent => sent.Header("Authorization")).ShouldBe(["Bearer token-1", "Bearer token-1"]);
    }

    [Fact]
    public async Task AHungAttemptEndsAsATimeoutAfterItsOwnTime()
    {
        var session = new FakeSession();
        using var handler = new RateLimitHandler(TimeProvider.System, session, TimeSpan.FromMilliseconds(50)) { InnerHandler = new HungServer() };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://cms.example.com/api/management/v1/me");

        // Awaited directly: Shouldly reports a canceled task with an exception of its own, which drops the inner one.
        TaskCanceledException? thrown = null;
        try
        {
            await invoker.SendAsync(request, Token);
        }
        catch (TaskCanceledException canceled)
        {
            thrown = canceled;
        }

        thrown.ShouldNotBeNull().InnerException.ShouldBeOfType<TimeoutException>();
        session.Notices.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheCallersCancellationIsNotATimeout()
    {
        using var handler = new RateLimitHandler(TimeProvider.System, new FakeSession()) { InnerHandler = new HungServer() };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://cms.example.com/api/management/v1/me");
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(Token);
        canceled.CancelAfter(TimeSpan.FromMilliseconds(50));

        var thrown = await Should.ThrowAsync<OperationCanceledException>(() => invoker.SendAsync(request, canceled.Token));

        thrown.InnerException.ShouldNotBeOfType<TimeoutException>();
    }

    private sealed class HungServer : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The delay never ends.");
        }
    }

    private static Action<HttpResponseMessage> RetryAfter(int seconds) =>
        response => response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
}
