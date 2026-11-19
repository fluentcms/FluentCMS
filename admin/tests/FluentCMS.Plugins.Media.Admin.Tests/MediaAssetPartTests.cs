using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Plugins.Media.Admin.Api;
using Microsoft.AspNetCore.Components.Web;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Admin.Tests;

// The alt text and the focal point of an asset, the parts the field editor of a media field reuses.
public sealed class MediaAssetPartTests : MediaTestContext
{
    private int Patches => Library.Requests.Count(request => request.Method == HttpMethod.Patch);

    // The requests after the part was given the asset: a save sent with the version read, its 412, and the read after it.
    private void RefusedWithoutAReadBefore(int readsBefore = 1)
    {
        Library.Requests.Select(request => request.Method.Method).ShouldBe([.. Enumerable.Repeat("GET", readsBefore), "PATCH", "GET"]);
        Library.Requests.Single(request => request.Method == HttpMethod.Patch).IfMatch.ShouldBe("\"1\"");
    }

    [Fact]
    public void TheAltTextIsSavedInTheLocaleTheAssetWasReadInAndEachLocaleKeepsItsOwn()
    {
        var id = Library.AddAsset("cat.png");
        MediaAssetInfo? changed = null;

        var english = Render<MediaAltText>(parameters => parameters.Add(part => part.Asset, Read(id)).Add(part => part.OnChanged, asset => changed = asset));
        Field(english, "Alt text").Input("A cat");
        Field(english, "Alt text").Blur();
        english.WaitForAssertion(() => changed.ShouldNotBeNull());
        changed!.Alt.ShouldBe("A cat");

        var french = Render<MediaAltText>(parameters => parameters.Add(part => part.Asset, Read(id, "fr")).Add(part => part.OnChanged, asset => changed = asset));
        Field(french, "Alt text").GetAttribute("value").ShouldBeNull();
        Field(french, "Alt text").GetAttribute("lang").ShouldBe("fr");
        Field(french, "Alt text").Input("Un chat");
        Field(french, "Alt text").Blur();
        french.WaitForAssertion(() => changed!.Alt.ShouldBe("Un chat"));

        Library.Text(id, "en", "alt").ShouldBe("A cat");
        Library.Text(id, "fr", "alt").ShouldBe("Un chat");
        Library.Requests.Where(request => request.Method == HttpMethod.Patch).Select(request => request.Locale).ShouldBe(["en", "fr"]);
    }

    [Fact]
    public void AnUnchangedTextIsNotSaved()
    {
        var id = Library.AddAsset("cat.png");

        var cut = Render<MediaAltText>(parameters => parameters.Add(part => part.Asset, Read(id)));
        Field(cut, "Alt text").Blur();

        Patches.ShouldBe(0);
    }

    [Fact]
    public void ASaveAfterAChangeElsewhereShowsTheConflictInsteadOfOverwriting()
    {
        var id = Library.AddAsset("cat.png");
        MediaAssetInfo? changed = null;
        var cut = Render<MediaAltText>(parameters => parameters.Add(part => part.Asset, Read(id)).Add(part => part.OnChanged, asset => changed = asset));
        Library.ChangeElsewhere(id, "Their title");

        Field(cut, "Alt text").Input("A cat");
        Field(cut, "Alt text").Blur();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldContain("changed elsewhere"));
        RefusedWithoutAReadBefore();
        Library.Text(id, "en", "alt").ShouldBeNull();
        changed.ShouldBeNull();

        Button(cut, "Save mine over it").Click();
        cut.WaitForAssertion(() => changed.ShouldNotBeNull());
        changed!.Alt.ShouldBe("A cat");
        changed.Title.ShouldBe("Their title");
        changed.ETag.ShouldBe("\"3\"");
        Library.Requests.Last(request => request.Method == HttpMethod.Patch).IfMatch.ShouldBeNull();
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void TheUserCanTakeTheOtherChangeInstead()
    {
        var id = Library.AddAsset("cat.png");
        MediaAssetInfo? changed = null;
        var cut = Render<MediaAltText>(parameters => parameters.Add(part => part.Asset, Read(id)).Add(part => part.OnChanged, asset => changed = asset));
        Library.ChangeElsewhere(id, "Their title");
        Field(cut, "Alt text").Input("A cat");
        Field(cut, "Alt text").Blur();
        cut.WaitForAssertion(() => HasButton(cut, "Use the other change").ShouldBeTrue());

        Button(cut, "Use the other change").Click();

        changed!.Title.ShouldBe("Their title");
        changed.Alt.ShouldBeNull();
        changed.ETag.ShouldBe("\"2\"");
        Patches.ShouldBe(1);
        Library.Text(id, "en", "alt").ShouldBeNull();

        // Given the asset it raised, the part shows the other change and has nothing left to save; the next save is based on it.
        cut.Render(parameters => parameters.Add(part => part.Asset, changed));
        Field(cut, "Alt text").GetAttribute("value").ShouldBeNull();
        Field(cut, "Alt text").Blur();
        Patches.ShouldBe(1);
        Field(cut, "Alt text").Input("A grey cat");
        Field(cut, "Alt text").Blur();
        cut.WaitForAssertion(() => Library.Text(id, "en", "alt").ShouldBe("A grey cat"));
        Library.Requests.Last(request => request.Method == HttpMethod.Patch).IfMatch.ShouldBe("\"2\"");
    }

    [Fact]
    public void TheSlidersAndTheImageRunLeftToRightInARightToLeftPage()
    {
        var id = Library.AddAsset("beach.png");

        var cut = Render<MediaFocalPoint>(parameters => parameters.Add(part => part.Asset, Read(id)));

        cut.Find(".media-focal-frame").GetAttribute("dir").ShouldBe("ltr");
        cut.FindAll(".media-focal-sliders input").ShouldAllBe(input => input.GetAttribute("dir") == "ltr");
        cut.FindAll(".media-focal-sliders input")[0].Change("80");
        cut.Find(".media-focal-marker").GetAttribute("style").ShouldBe("left:80%;top:50%");
    }

    [Fact]
    public void ARefusalIsShown()
    {
        var id = Library.AddAsset("cat.png");
        Library.PatchProblem = new ApiProblem { Status = 403 };
        var cut = Render<MediaAltText>(parameters => parameters.Add(part => part.Asset, Read(id)));

        Field(cut, "Alt text").Input("A cat");
        Field(cut, "Alt text").Blur();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldNotBeEmpty());
    }

    [Fact]
    public void AReadOnlyAltTextIsNotSaved()
    {
        var id = Library.AddAsset("cat.png");
        var cut = Render<MediaAltText>(parameters => parameters.Add(part => part.Asset, Read(id)).Add(part => part.ReadOnly, true));

        Field(cut, "Alt text").HasAttribute("readonly").ShouldBeTrue();
        Field(cut, "Alt text").Input("A cat");
        Field(cut, "Alt text").Blur();

        Patches.ShouldBe(0);
    }

    [Fact]
    public void AClickOnTheImageSetsTheFocalPoint()
    {
        var id = Library.AddAsset("beach.png");
        MediaAssetInfo? changed = null;
        var cut = Render<MediaFocalPoint>(parameters => parameters.Add(part => part.Asset, Read(id)).Add(part => part.OnChanged, asset => changed = asset));

        // 640 by 480 is drawn at 320 by 240.
        var image = cut.Find(".media-focal-image img");
        image.GetAttribute("width").ShouldBe("320");
        image.GetAttribute("height").ShouldBe("240");
        cut.Find(".media-focal-marker").GetAttribute("style").ShouldBe("left:50%;top:50%");
        image.Click(new MouseEventArgs { OffsetX = 80, OffsetY = 180 });

        cut.WaitForAssertion(() => changed.ShouldNotBeNull());
        changed!.FocalPoint.ShouldBe(new MediaPoint { X = 0.25, Y = 0.75 });
        Library.Asset(id)["focalPoint"]!.ToJsonString().ShouldBe("""{"x":0.25,"y":0.75}""");
    }

    [Fact]
    public void TheArrowKeysOnTheSlidersSetTheFocalPointWithOneSaveForARun()
    {
        var id = Library.AddAsset("beach.png");
        var asset = Read(id);
        var cut = Render<MediaFocalPoint>(parameters => parameters.Add(part => part.Asset, asset).Add(part => part.OnChanged, changed => asset = changed));
        cut.FindAll(".media-focal-sliders input")[0].GetAttribute("aria-valuetext").ShouldBe("50 %");

        // Each arrow key is an input and a change of the native slider.
        foreach (var value in new[] { "51", "52", "53" })
        {
            cut.FindAll(".media-focal-sliders input")[0].Input(value);
            cut.FindAll(".media-focal-sliders input")[0].Change(value);
        }

        cut.Find(".media-focal-marker").GetAttribute("style").ShouldBe("left:53%;top:50%");
        cut.WaitForAssertion(() => asset.FocalPoint.ShouldBe(new MediaPoint { X = 0.53, Y = 0.5 }), TimeSpan.FromSeconds(3));
        Patches.ShouldBe(1);

        // The holder gives the part the asset it raised, which the next save is based on.
        cut.Render(parameters => parameters.Add(part => part.Asset, asset));
        cut.FindAll(".media-focal-sliders input")[1].Change("20");
        cut.WaitForAssertion(() => asset.FocalPoint.ShouldBe(new MediaPoint { X = 0.53, Y = 0.2 }), TimeSpan.FromSeconds(3));
        cut.Find(".media-focal-preview img").GetAttribute("style").ShouldBe("object-position:53% 20%");
    }

    [Fact]
    public void AFocalPointSavedAfterAChangeElsewhereShowsTheConflict()
    {
        var id = Library.AddAsset("beach.png");
        var cut = Render<MediaFocalPoint>(parameters => parameters.Add(part => part.Asset, Read(id)));
        Library.ChangeElsewhere(id, "Their title");

        cut.Find(".media-focal-image img").Click(new MouseEventArgs { OffsetX = 0, OffsetY = 0 });

        cut.WaitForAssertion(() => HasButton(cut, "Save mine over it").ShouldBeTrue());
        RefusedWithoutAReadBefore();
        Library.Asset(id)["focalPoint"].ShouldBeNull();
    }

    [Fact]
    public void AReadOnlyFocalPointTakesNoClick()
    {
        var id = Library.AddAsset("beach.png");
        var cut = Render<MediaFocalPoint>(parameters => parameters.Add(part => part.Asset, Read(id)).Add(part => part.ReadOnly, true));

        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        cut.Find(".media-focal-image img").Click(new MouseEventArgs { OffsetX = 10, OffsetY = 10 });

        Patches.ShouldBe(0);
    }

    [Fact]
    public void AFileThatIsNotAnImageHasNoFocalPoint()
    {
        var id = Library.AddAsset("report.pdf", mimeType: "application/pdf");

        var cut = Render<MediaFocalPoint>(parameters => parameters.Add(part => part.Asset, Read(id)));

        cut.Markup.Trim().ShouldBeEmpty();
    }

    private MediaAssetInfo Read(Guid id, string locale = "en") =>
        new MediaApi(Library).GetAsync(id, locale, CancellationToken.None).GetAwaiter().GetResult();
}
