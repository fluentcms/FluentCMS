using System.Threading.RateLimiting;
using FluentCMS.Core.Api.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Common;

public sealed class FluentCmsRateLimitPolicyTests
{
    private readonly AuthRateLimitPolicy _policy = new(Limits(auth: 3), new FixedClock());

    // The partitioned limiter disposes a bucket that has been full for a while, with DisposeAsync, which a slow request may
    // see before its response starts; disposing the partitioned limiter disposes the bucket the same way.
    [Fact]
    public async Task TheHeadersComeFromTheBucketThatCountedTheRequestEvenOnceItIsDisposed()
    {
        var limiter = PartitionedRateLimiter.Create<HttpContext, string>(_policy.GetPartition);
        var request = new Request();

        using (var lease = await limiter.AcquireAsync(request.Context, cancellationToken: TestContext.Current.CancellationToken))
        {
            lease.IsAcquired.ShouldBeTrue();
        }

        await limiter.DisposeAsync();
        await request.StartAsync();

        request.Header("RateLimit-Limit").ShouldBe("3");
        request.Header("RateLimit-Remaining").ShouldBe("2");
        request.Header("RateLimit-Reset").ShouldBe("20");
    }

    [Fact]
    public async Task EachResponseReportsTheBucketAsItsOwnRequestLeftIt()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(_policy.GetPartition);
        var first = new Request();
        var second = new Request();

        (await limiter.AcquireAsync(first.Context, cancellationToken: TestContext.Current.CancellationToken)).Dispose();
        (await limiter.AcquireAsync(second.Context, cancellationToken: TestContext.Current.CancellationToken)).Dispose();
        await first.StartAsync();
        await second.StartAsync();

        first.Header("RateLimit-Remaining").ShouldBe("2");
        first.Header("RateLimit-Reset").ShouldBe("20");
        second.Header("RateLimit-Remaining").ShouldBe("1");
        second.Header("RateLimit-Reset").ShouldBe("40");
    }

    // The rate limiter tries a refused request again, to wait for a permit, so it asks for the partition twice.
    [Fact]
    public async Task ARefusedRequestReportsTheEmptyBucketOnce()
    {
        var policy = new AuthRateLimitPolicy(Limits(auth: 1), new FixedClock());
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(policy.GetPartition);
        (await limiter.AcquireAsync(new Request().Context, cancellationToken: TestContext.Current.CancellationToken)).Dispose();
        var refused = new Request();

        using (var first = limiter.AttemptAcquire(refused.Context))
        using (var second = await limiter.AcquireAsync(refused.Context, cancellationToken: TestContext.Current.CancellationToken))
        {
            first.IsAcquired.ShouldBeFalse();
            second.IsAcquired.ShouldBeFalse();
        }

        await refused.StartAsync();

        refused.Starting.ShouldBe(1);
        refused.Header("RateLimit-Limit").ShouldBe("1");
        refused.Header("RateLimit-Remaining").ShouldBe("0");
        refused.Header("RateLimit-Reset").ShouldBe("60");
    }

    [Fact]
    public async Task ThePolicyBeforeAuthenticationAddsNoHeaders()
    {
        var policy = new PreAuthRateLimitPolicy(new RateLimitOptions(), new FixedClock());
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(policy.GetPartition);
        var request = new Request();

        (await limiter.AcquireAsync(request.Context, cancellationToken: TestContext.Current.CancellationToken)).Dispose();
        await request.StartAsync();

        request.Starting.ShouldBe(0);
        request.Header("RateLimit-Limit").ShouldBeEmpty();
    }

    // The endpoint's limiter keeps the first limiter it gets for a key, so a request counted before authentication and one
    // the endpoint counts itself, from the same address, must not share a key in either order.
    [Fact]
    public async Task ARequestCountedBeforeAuthenticationLeavesTheCallersBucketToTheNextRequest()
    {
        var policy = new AuthRateLimitPolicy(Limits(auth: 1), new FixedClock());
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(policy.GetPartition);
        var counted = new Request();
        policy.MarkCounted(counted.Context);

        (await limiter.AcquireAsync(counted.Context, cancellationToken: TestContext.Current.CancellationToken)).Dispose();
        using var first = await limiter.AcquireAsync(new Request().Context, cancellationToken: TestContext.Current.CancellationToken);
        using var second = await limiter.AcquireAsync(new Request().Context, cancellationToken: TestContext.Current.CancellationToken);

        first.IsAcquired.ShouldBeTrue();
        second.IsAcquired.ShouldBeFalse();
    }

    [Fact]
    public async Task ARequestCountedBeforeAuthenticationIsNotCountedAgainInTheCallersBucket()
    {
        var policy = new AuthRateLimitPolicy(Limits(auth: 1), new FixedClock());
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(policy.GetPartition);
        var counted = new Request();
        policy.MarkCounted(counted.Context);

        using var first = await limiter.AcquireAsync(new Request().Context, cancellationToken: TestContext.Current.CancellationToken);
        using var passed = await limiter.AcquireAsync(counted.Context, cancellationToken: TestContext.Current.CancellationToken);
        await counted.StartAsync();

        first.IsAcquired.ShouldBeTrue();
        passed.IsAcquired.ShouldBeTrue();
        counted.Starting.ShouldBe(0);
    }

    private static RateLimitOptions Limits(int auth)
    {
        var options = new RateLimitOptions();
        options.Auth.PermitLimit = auth;
        return options;
    }

    private sealed class Request
    {
        private readonly StartingResponse _response = new();

        public Request()
        {
            Context = new DefaultHttpContext();
            Context.Features.Set<IHttpResponseFeature>(_response);
        }

        public HttpContext Context { get; }

        public int Starting => _response.Callbacks;

        public Task StartAsync() => _response.StartAsync();

        public string Header(string name) => Context.Response.Headers[name].ToString();
    }

    // Runs the OnStarting callbacks when told to, as a server does when the response starts.
    private sealed class StartingResponse : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _starting = [];

        public int Callbacks => _starting.Count;

        public override void OnStarting(Func<object, Task> callback, object state) => _starting.Add((callback, state));

        public async Task StartAsync()
        {
            foreach (var (callback, state) in _starting)
            {
                await callback(state);
            }
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    }
}
