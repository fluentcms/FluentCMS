using FluentCMS.Core.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.DependencyInjection;

public sealed class ProxyAddressTests
{
    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2001:DB8:0:0:0:0:0:1")]
    [InlineData("::ffff:10.0.0.1")]
    public void AFullAddressIsAccepted(string text) => ProxyAddress.TryParse(text, out _).ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("10.1")]
    [InlineData("10.0.1")]
    [InlineData("010.0.0.1")]
    [InlineData("0x0a.0.0.1")]
    [InlineData("167772161")]
    [InlineData("10.0.0.1 ")]
    [InlineData("fe80::1%eth0")]
    [InlineData("proxy")]
    public void ALegacyOrBrokenFormIsRefused(string text) => ProxyAddress.TryParse(text, out _).ShouldBeFalse();

    [Theory]
    [InlineData("10.0.0.0/8", 8)]
    [InlineData("0.0.0.0/0", 0)]
    [InlineData("2001:db8::/32", 32)]
    public void ANetworkInCidrNotationIsAccepted(string text, int prefix)
    {
        ProxyAddress.TryParseNetwork(text, out var network).ShouldBeTrue();
        network.PrefixLength.ShouldBe(prefix);
    }

    [Theory]
    [InlineData("10.0.0.0")]
    [InlineData("10/8")]
    [InlineData("10.0.0.0/")]
    [InlineData("10.0.0.0/+8")]
    [InlineData("10.0.0.0/ 8")]
    [InlineData("10.0.0.0/33")]
    [InlineData("/8")]
    public void ALooseNetworkIsRefused(string text) => ProxyAddress.TryParseNetwork(text, out _).ShouldBeFalse();
}
