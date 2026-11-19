using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Admin.Tests;

public sealed class MediaTypesTests
{
    [Theory]
    [InlineData("image/*", "image/png", true)]
    [InlineData("IMAGE/*", "image/png", true)]
    [InlineData("image/*", "image/", false)]
    [InlineData("image/*", "imagery/png", false)]
    [InlineData("application/pdf", "Application/PDF; charset=x", true)]
    [InlineData("image/png", "image/jpeg", false)]
    public void ATypeIsAcceptedAsTheServerAcceptsIt(string accept, string mimeType, bool accepted) =>
        MediaTypes.Accepts([accept], mimeType).ShouldBe(accepted);
}
