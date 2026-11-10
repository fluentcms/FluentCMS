using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Delivery;

public sealed class DeliveryCacheOptionsTests
{
    [Fact]
    public void TheDefaultsAreThoseOfTheSpecification()
    {
        var options = new DeliveryCacheOptions();

        options.CacheControl.ShouldBe("public, max-age=60, stale-while-revalidate=300");
        options.OutputCache.ShouldBeTrue();
        options.OutputCacheDuration.ShouldBe(TimeSpan.FromSeconds(60));
        options.SharedCacheControl.ShouldBe(options.CacheControl);
        options.CacheTagHeader.ShouldBeNull();
        options.TagHeader.ShouldBeNull();
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(false, null, false)]
    [InlineData(false, "", false)]
    [InlineData(false, "Cache-Tag", true)]
    public void TheEvictionsRunWhileTheOutputCacheOrTheTagHeaderNeedsThem(bool outputCache, string? header, bool invalidates)
    {
        var options = new DeliveryCacheOptions { OutputCache = outputCache, CacheTagHeader = header };

        options.Validate();

        options.Invalidates.ShouldBe(invalidates);
    }

    [Theory]
    [InlineData("Cache-Tag")]
    [InlineData("Surrogate-Key")]
    [InlineData("Edge-Cache-Tag")]
    [InlineData("X_Tags.v2")]
    public void AHeaderNameIsAccepted(string header) => new DeliveryCacheOptions { CacheTagHeader = header }.Validate();

    [Theory]
    [InlineData("Cache Tag")]
    [InlineData(" ")]
    [InlineData("Cache-Tag:")]
    [InlineData("Tägs")]
    [InlineData("cache-control")]
    [InlineData("Vary")]
    [InlineData("Set-Cookie")]
    [InlineData("Transfer-Encoding")]
    public void ATagHeaderThatIsNoNameOrOneTheResponseWritesIsRefused(string header)
    {
        var exception = Should.Throw<FluentCmsConfigurationException>(new DeliveryCacheOptions { CacheTagHeader = header }.Validate);

        exception.Code.ShouldBe("FCMS1101");
        exception.Detail.ShouldStartWith("CacheTagHeader");
    }

    [Theory]
    [InlineData("public, max-age=60, stale-while-revalidate=300", "max-age=60, private, stale-while-revalidate=300")]
    [InlineData("public, s-maxage=600, max-age=60", "max-age=60, private")]
    [InlineData("no-store", "no-store, private")]
    public void ACallerWithCredentialsGetsThePrivateFormOfTheValue(string configured, string expected)
    {
        var options = new DeliveryCacheOptions { CacheControl = configured };

        options.SharedCacheControl.ShouldBe(configured);
        options.PrivateCacheControl.ShouldBe(expected);
    }

    [Theory]
    [InlineData("max-age=soon", null, "CacheControl")]
    [InlineData("", null, "CacheControl")]
    [InlineData(DeliveryCacheOptions.DefaultCacheControl, "00:00:00", "OutputCacheDuration")]
    [InlineData(DeliveryCacheOptions.DefaultCacheControl, "-00:00:01", "OutputCacheDuration")]
    public void ABadValueIsRefusedWithItsKey(string cacheControl, string? duration, string key)
    {
        var options = new DeliveryCacheOptions { CacheControl = cacheControl };
        if (duration is not null)
        {
            options.OutputCacheDuration = TimeSpan.Parse(duration, System.Globalization.CultureInfo.InvariantCulture);
        }

        var exception = Should.Throw<FluentCmsConfigurationException>(options.Validate);

        exception.Code.ShouldBe("FCMS1101");
        exception.Detail.ShouldStartWith(key);
    }
}
