using FluentCMS.Core.Media;

namespace FluentCMS.Tests.Media;

public sealed class AssetMetadataTests
{
    [Fact]
    public void Metadata_is_stored_trimmed_with_each_tag_once_in_lowercase()
    {
        var metadata = AssetMetadata.Create(" drill.jpg ", "/products/drills/", ["Product-Shot", "hero", "product-shot"], " A cordless drill. ", null).Value;

        Assert.Equal("drill.jpg", metadata.FileName);
        Assert.Equal("products/drills", metadata.Folder);
        Assert.Equal(["product-shot", "hero"], metadata.Tags);
        Assert.Equal("A cordless drill.", metadata.AltText);
        Assert.Null(metadata.Caption);
    }

    [Fact]
    public void Every_problem_comes_back_at_once_and_names_its_field()
    {
        var errors = AssetMetadata.Create("../drill.jpg", "Products/Drills", ["not a tag"], " ", new string('x', 2001)).Errors;

        Assert.Equal(
            [
                MediaErrorCodes.FileNameInvalid,
                MediaErrorCodes.FolderInvalid,
                MediaErrorCodes.TagInvalid,
                MediaErrorCodes.AltTextInvalid,
                MediaErrorCodes.CaptionInvalid,
            ],
            errors.Select(error => error.Code));
        Assert.All(errors, error => Assert.Contains(':', error.Message));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a\\b.png")]
    [InlineData("tab\t.png")]
    public void A_file_name_is_one_name_without_slashes(string fileName)
    {
        Assert.Equal(MediaErrorCodes.FileNameInvalid, Assert.Single(AssetMetadata.Create(fileName, null, null, null, null).Errors).Code);
    }

    [Fact]
    public void Alt_text_and_caption_keep_line_breaks_and_refuse_other_control_characters()
    {
        var multiline = AssetMetadata.Create("a.png", null, null, "A drill.\nOn a bench.", "Line one\r\n\tline two");
        var withNul = AssetMetadata.Create("a.png", null, null, "alt\0text", "bell\a");

        Assert.True(multiline.IsSuccess);
        Assert.Equal([MediaErrorCodes.AltTextInvalid, MediaErrorCodes.CaptionInvalid], withNul.Errors.Select(error => error.Code));
    }

    [Fact]
    public void An_asset_has_at_most_32_tags()
    {
        var tags = Enumerable.Range(0, 33).Select(index => $"tag-{index}");

        Assert.Equal(MediaErrorCodes.TagInvalid, Assert.Single(AssetMetadata.Create("a.png", null, tags, null, null).Errors).Code);
    }
}
