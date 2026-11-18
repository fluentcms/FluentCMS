using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Admin.Tests;

public sealed class MediaListCellTests : MediaTestContext
{
    [Fact]
    public void APopulatedImageShowsItsThumbnailAndTitle()
    {
        var id = Guid.NewGuid();
        var cut = Cell(Populated(id, "hero.png", "image/png", "Hero"));

        cut.Find(".media-cell-name").TextContent.ShouldBe("Hero");
        cut.Find(".media-cell-name").GetAttribute("lang").ShouldBe("en");
        cut.Find("img").GetAttribute("src").ShouldBe($"https://cms.example.com/media/{id:D}/original/hero.png?sig=abc");
        cut.Find("img").GetAttribute("alt").ShouldBe("");
        cut.FindAll(".media-cell-more").ShouldBeEmpty();
        cut.FindAll("button, a, [tabindex]").ShouldBeEmpty();
    }

    [Fact]
    public void SeveralAssetsShowTheFirstAndHowManyMore()
    {
        var cut = Cell(new JsonArray(
            Populated(Guid.NewGuid(), "a.png", "image/png"),
            Populated(Guid.NewGuid(), "b.png", "image/png"),
            Populated(Guid.NewGuid(), "c.png", "image/png")));

        cut.Find(".media-cell-name").TextContent.ShouldBe("a.png");
        cut.Find(".media-cell-more").TextContent.ShouldBe("+2 more");
    }

    [Fact]
    public void AFileThatIsNotAnImageOrAnAssetTheReadDidNotPopulateShowsNoImage()
    {
        Cell(Populated(Guid.NewGuid(), "guide.pdf", "application/pdf")).FindAll("img").ShouldBeEmpty();

        var id = Guid.NewGuid();
        var reference = Cell(new JsonObject { ["id"] = id.ToString("D"), ["type"] = "media" });
        reference.FindAll("img").ShouldBeEmpty();
        reference.Find(".media-cell-name").TextContent.ShouldBe(id.ToString("D"));
    }

    [Fact]
    public void AnAssetTheReadLeftNullIsCounted() =>
        Cell(new JsonArray(Populated(Guid.NewGuid(), "a.png", "image/png"), null, Populated(Guid.NewGuid(), "c.png", "image/png")))
            .Find(".media-cell-more").TextContent.ShouldBe("+2 more");

    [Fact]
    public void AnEmptyFieldShowsNothing() => Cell(null).Markup.Trim().ShouldBeEmpty();

    private static JsonObject Populated(Guid id, string fileName, string mimeType, string? title = null) => new()
    {
        ["id"] = id.ToString("D"),
        ["type"] = "media",
        ["fileName"] = fileName,
        ["mimeType"] = mimeType,
        ["size"] = 2048,
        ["title"] = title,
        ["urls"] = new JsonObject { ["original"] = $"/media/{id:D}/original/{fileName}?sig=abc", ["presets"] = new JsonObject() },
    };

    private IRenderedComponent<MediaListCell> Cell(JsonNode? value) => Render<MediaListCell>(parameters => parameters
        .Add(cell => cell.Field, new SchemaFieldDescription { Name = "hero", Type = "media", Validation = new FieldValidationDescription() })
        .Add(cell => cell.Entry, new ContentEntry { Id = Guid.NewGuid(), Locale = "en", Fields = new JsonObject { ["hero"] = value } }));
}
