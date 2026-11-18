using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Admin.Tests;

public sealed class MediaFieldEditorTests : MediaTestContext
{
    private readonly List<JsonNode?> changes = [];

    [Fact]
    public void AnAssetShowsItsThumbnailAndTitleReadInTheContentLocale()
    {
        var hero = Library.AddAsset("hero.png", title: "Hero");

        var cut = Edit(Field(), new JsonObject { ["id"] = hero.ToString("D"), ["type"] = "media" }, locale: "fr");

        cut.WaitForAssertion(() => cut.Find(".media-field-name").TextContent.ShouldBe("Hero"));
        cut.Find(".media-field-name").GetAttribute("lang").ShouldBe("fr");
        cut.Find(".media-field-thumb img").GetAttribute("src").ShouldBe($"https://cms.example.com/media/{hero:D}/thumbnail/hero.png");
        cut.Find("legend").TextContent.Trim().ShouldBe("Hero");
        Library.Requests.Single(request => request.Path == $"media/{hero:D}").Locale.ShouldBe("fr");
        HasButton(cut, "Choose an asset").ShouldBeTrue();
    }

    [Fact]
    public async Task ChoosingAnAssetInThePickerWritesItsId()
    {
        Library.AddAsset("logo.png");
        var beach = Library.AddAsset("beach.jpg", mimeType: "image/jpeg");
        var cut = Edit(Field(), null);
        cut.Find(".media-field-empty").TextContent.ShouldBe("No asset chosen.");

        var picking = cut.InvokeAsync(() => Button(cut, "Choose an asset").Click());
        cut.WaitForAssertion(() => Overlays.Shown.ShouldHaveSingleItem());
        var host = Render(Overlays.Host);
        host.WaitForAssertion(() => host.FindAll(".media-tile").Count.ShouldBe(2));
        host.FindAll(".media-tile-open")[1].Click();
        host.WaitForAssertion(() => host.FindComponent<CmsDialog>().Instance.Open.ShouldBeFalse());
        await host.InvokeAsync(() => host.FindComponent<CmsDialog>().Instance.OnClosed.InvokeAsync());
        await picking;

        cut.WaitForAssertion(() => changes.ShouldHaveSingleItem()!.GetValue<string>().ShouldBe(beach.ToString("D")));
    }

    [Fact]
    public void ThePickerIsHeldToTheTypesOfTheFieldAndStartsWithItsAssets()
    {
        var logo = Library.AddAsset("logo.png");
        Library.AddAsset("guide.pdf", mimeType: "application/pdf");
        var field = Field(multiple: true) with { Settings = new JsonObject { ["accept"] = new JsonArray("image/*") } };
        var cut = Edit(field, new JsonArray(logo.ToString("D")));
        cut.WaitForAssertion(() => cut.Find(".media-field-name").TextContent.ShouldBe("logo.png"));

        _ = cut.InvokeAsync(() => Button(cut, "Choose assets").Click());
        cut.WaitForAssertion(() => Overlays.Shown.ShouldHaveSingleItem());
        var host = Render(Overlays.Host);

        host.WaitForAssertion(() => host.Markup.ShouldContain("1 asset chosen"));
        host.WaitForAssertion(() => host.FindAll(".media-tile-name").Select(name => name.TextContent).ShouldBe(["logo.png"]));
    }

    [Fact]
    public async Task SeveralAssetsAreRemovedAndOrderedByButtonsAndByAltWithTheArrows()
    {
        var first = Library.AddAsset("a.png");
        var second = Library.AddAsset("b.png");
        var third = Library.AddAsset("c.png");
        var cut = Edit(Field(multiple: true), Ids(first, second, third));
        cut.WaitForAssertion(() => Names(cut).ShouldBe(["a.png", "b.png", "c.png"]));
        cut.Find("fieldset").GetAttribute("aria-describedby").ShouldNotBeNull();

        Labeled(cut, "Move a.png down").Click();
        Written().ShouldBe([second, first, third]);
        Announcer.Messages.Last().ShouldBe("a.png moved to position 2 of 3.");

        cut.Render(parameters => parameters.Add(editor => editor.Value, Ids(second, first, third)));
        await cut.FindAll(".media-field-item")[2].TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });
        Written().ShouldBe([second, third, first]);

        cut.Render(parameters => parameters.Add(editor => editor.Value, Ids(second, third, first)));
        Labeled(cut, "Remove c.png").Click();
        Written().ShouldBe([second, first]);
        Announcer.Messages.Last().ShouldBe("c.png removed.");
    }

    [Fact]
    public void ASingleAssetIsRemovedToNull()
    {
        var hero = Library.AddAsset("hero.png");
        var cut = Edit(Field(), JsonValue.Create(hero.ToString("D")));
        cut.WaitForAssertion(() => Names(cut).ShouldBe(["hero.png"]));
        cut.FindAll("fluent-button[aria-label='Move hero.png up']").ShouldBeEmpty();

        Labeled(cut, "Remove hero.png").Click();

        changes.ShouldHaveSingleItem().ShouldBeNull();
    }

    [Fact]
    public void AReadOnlyFieldShowsItsAssetsWithoutAButtonAndTheErrorsAndHelpOfTheField()
    {
        var hero = Library.AddAsset("hero.png");
        var field = Field() with { Display = new FieldDisplayDescription { Help = "The banner." } };
        var cut = Render<MediaFieldEditor>(parameters => parameters
            .Add(editor => editor.Field, field)
            .Add(editor => editor.Value, JsonValue.Create(hero.ToString("D")))
            .Add(editor => editor.ReadOnly, true));
        cut.WaitForAssertion(() => Names(cut).ShouldBe(["hero.png"]));
        cut.FindAll("fluent-button").ShouldBeEmpty();
        cut.Find(".cms-field-message").TextContent.ShouldBe("The banner.");

        cut.Render(parameters => parameters.Add(editor => editor.Errors, [new ApiProblemError { Path = "hero", Code = "FCMS1001", Message = "Is required." }]));
        cut.Find(".cms-field-message-error").TextContent.ShouldBe("Is required.");
    }

    [Fact]
    public void ACallerWithoutMediaReadIsNotOfferedThePicker()
    {
        Admin.Permissions.Clear();
        var cut = Edit(Field(), null);
        HasButton(cut, "Choose an asset").ShouldBeFalse();
    }

    [Fact]
    public void AnAssetThatCannotBeReadShowsItsId()
    {
        var gone = Guid.NewGuid();
        var cut = Edit(Field(), JsonValue.Create(gone.ToString("D")));
        cut.WaitForAssertion(() => Names(cut).ShouldBe([gone.ToString("D")]));
        cut.Find(".media-field-thumb").InnerHtml.ShouldNotContain("<img");
    }

    private static AngleSharp.Dom.IElement Labeled(IRenderedComponent<MediaFieldEditor> cut, string label) =>
        cut.Find($"fluent-button[aria-label='{label}']");

    private static SchemaFieldDescription Field(bool multiple = false) =>
        new() { Name = "hero", Type = "media", Multiple = multiple, Validation = new FieldValidationDescription() };

    private static JsonArray Ids(params Guid[] ids) => new([.. ids.Select(id => (JsonNode)JsonValue.Create(id.ToString("D")))]);

    private static IReadOnlyList<string> Names(IRenderedComponent<MediaFieldEditor> cut) =>
        [.. cut.FindAll(".media-field-name").Select(name => name.TextContent)];

    private IReadOnlyList<Guid> Written() => [.. changes[^1]!.AsArray().Select(id => Guid.Parse(id!.GetValue<string>()))];

    private IRenderedComponent<MediaFieldEditor> Edit(SchemaFieldDescription field, JsonNode? value, string locale = "en") =>
        Render<MediaFieldEditor>(parameters => parameters
            .Add(editor => editor.Field, field)
            .Add(editor => editor.Value, value)
            .Add(editor => editor.ContentLocale, locale)
            .Add(editor => editor.ValueChanged, EventCallback.Factory.Create<JsonNode?>(this, changed => changes.Add(changed))));
}
