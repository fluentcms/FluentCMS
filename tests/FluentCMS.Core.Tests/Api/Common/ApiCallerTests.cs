using System.Net;
using System.Security.Claims;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Common;

public sealed class ApiCallerTests
{
    private static readonly string KeyId = Guid.CreateVersion7().ToString("D");

    [Fact]
    public void TheCallerAndItsCacheKeyAreReadOncePerRequest()
    {
        var context = Request(Key((CmsClaimTypes.RateLimit, "50")));

        var caller = ApiCaller.Of(context);

        ApiCaller.Of(context).ShouldBeSameAs(caller);
        caller.KeyCacheKey.ShouldBeSameAs(caller.KeyCacheKey);
        caller.KeyCacheKey.ShouldStartWith("key:");
        caller.IsApiKey.ShouldBeTrue();
        caller.Subject.ShouldBe(KeyId);
        caller.RateLimit.ShouldBe(50);
    }

    [Fact]
    public void TheLimitsBeforeAndAfterAuthenticationShareOneAddress()
    {
        var context = Request(new ClaimsPrincipal(new ClaimsIdentity()));

        var first = ApiCaller.Address(context);

        first.ShouldBe("ip:10.0.0.1");
        ApiCaller.Address(context).ShouldBeSameAs(first);
    }

    [Fact]
    public void TheDeliveryPolicyPartitionsAKeyByItsIdAndNamesOnlyALimitOfItsOwn()
    {
        var options = new RateLimitOptions();
        var policy = new DeliveryRateLimitPolicy(options, TimeProvider.System);

        policy.GetPartition(Request(Key())).PartitionKey.ShouldBe($"key:{KeyId}");
        policy.GetPartition(Request(Key((CmsClaimTypes.RateLimit, "50")))).PartitionKey.ShouldBe($"key:{KeyId}|50");
        policy.GetPartition(Request(new ClaimsPrincipal(new ClaimsIdentity()))).PartitionKey.ShouldBe("ip:10.0.0.1");
    }

    [Fact]
    public void TheCacheKeyFollowsTheClaimsWhateverTheirOrder()
    {
        var one = ApiCaller.Of(Request(Key(("roles", "a"), ("scope", "delivery")))).KeyCacheKey;
        var reordered = ApiCaller.Of(Request(Key(("scope", "delivery"), ("roles", "a")))).KeyCacheKey;
        var other = ApiCaller.Of(Request(Key(("roles", "b"), ("scope", "delivery")))).KeyCacheKey;

        reordered.ShouldBe(one);
        other.ShouldNotBe(one);
    }

    // Each type and value is hashed with its length, so that moving a separator between them makes another key.
    [Fact]
    public void ClaimsThatWouldJoinToTheSameTextHaveDifferentCacheKeys()
    {
        var one = ApiCaller.Of(Request(Key(("a", "b=c")))).KeyCacheKey;
        var other = ApiCaller.Of(Request(Key(("a=b", "c")))).KeyCacheKey;

        other.ShouldNotBe(one);
    }

    private static ClaimsPrincipal Key(params (string Type, string Value)[] claims) => new(new ClaimsIdentity(
        [
            new Claim(CmsClaimTypes.Subject, KeyId),
            new Claim(CmsClaimTypes.Kind, nameof(PrincipalKind.ApiKey)),
            .. claims.Select(claim => new Claim(claim.Type, claim.Value)),
        ],
        "test"));

    private static DefaultHttpContext Request(ClaimsPrincipal user)
    {
        var context = new DefaultHttpContext { User = user };
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        return context;
    }
}
