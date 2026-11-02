using FluentCMS.Authorization;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Authorization;

public class PermissionTests
{
    [Theory]
    [InlineData("media.manage")]
    [InlineData("settings.media.manage")]
    [InlineData("web-hooks.read2")]
    public void AcceptsDottedLowerCaseNames(string name)
    {
        Permission.IsValidName(name).ShouldBeTrue();
        new Permission(name, "Does something.").Name.ShouldBe(name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("manage")]
    [InlineData("Media.manage")]
    [InlineData("media..manage")]
    [InlineData("media.manage.")]
    [InlineData("media.1manage")]
    [InlineData("media.*")]
    [InlineData("media.manage\n")]
    [InlineData("media manage")]
    public void RejectsAnythingElse(string? name)
    {
        Permission.IsValidName(name).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => new Permission(name!, "Does something."));
    }

    [Fact]
    public void NeedsADescription() =>
        Should.Throw<ArgumentException>(() => new Permission("media.manage", " "));
}
