using FluentCMS.Admin;

namespace FluentCMS.Tests.Admin;

public sealed class AdminRoutesTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/ui-spike")]
    [InlineData("/entries?type=page&page=2")]
    public void A_path_on_this_site_is_local(string url)
    {
        Assert.True(AdminRoutes.IsLocal(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/\r\nLocation: https://evil.example")]
    public void Anything_that_can_leave_the_site_is_not(string? url)
    {
        Assert.False(AdminRoutes.IsLocal(url));
    }
}
