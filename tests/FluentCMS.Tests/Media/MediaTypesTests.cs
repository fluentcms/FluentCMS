using FluentCMS.Core.Media;

namespace FluentCMS.Tests.Media;

public sealed class MediaTypesTests
{
    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("application/pdf", true)]
    [InlineData("video/webm", true)]
    [InlineData("text/html", false)]
    [InlineData("application/xhtml+xml", false)]
    [InlineData("image/svg+xml", false)]
    [InlineData("Image/JPEG", false)]
    [InlineData("image/jpeg; charset=x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_types_a_browser_does_not_run_as_a_document_are_allowed(string? type, bool allowed)
    {
        Assert.Equal(allowed, MediaTypes.IsAllowed(type));
    }

    [Theory]
    [InlineData("image/jpeg", "FFD8FFE000104A464946")]
    [InlineData("image/png", "89504E470D0A1A0A0000000D")]
    [InlineData("image/gif", "474946383961")]
    [InlineData("image/webp", "52494646240000005745425056503820")]
    [InlineData("application/pdf", "255044462D312E37")]
    [InlineData("video/mp4", "0000001866747970")]
    [InlineData("video/webm", "1A45DFA3")]
    public void Each_allowed_type_matches_its_signature(string type, string head)
    {
        Assert.True(MediaTypes.Matches(type, Convert.FromHexString(head)));
    }

    [Theory]
    [InlineData("image/jpeg", "3C68746D6C3E")]
    [InlineData("image/png", "FFD8FFE0")]
    [InlineData("image/webp", "52494646240000004156492020")]
    [InlineData("video/mp4", "000000")]
    [InlineData("text/html", "3C68746D6C3E")]
    public void A_file_that_does_not_start_like_its_type_does_not_match(string type, string head)
    {
        Assert.False(MediaTypes.Matches(type, Convert.FromHexString(head)));
    }
}
