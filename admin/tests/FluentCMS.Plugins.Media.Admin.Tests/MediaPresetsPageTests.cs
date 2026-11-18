using System.Reflection;
using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Plugins;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Admin.Tests;

public sealed class MediaPresetsPageTests : MediaTestContext
{
    [Fact]
    public void ThePageIsForACallerWhoManagesPresets() =>
        typeof(MediaPresetsPage).GetCustomAttribute<AdminPageAttribute>()!.Permission.ShouldBe(MediaPermissions.ManagePresets);

    [Fact]
    public void AnAdministratorAddsChangesAndDeletesAPreset()
    {
        var cut = Render<MediaPresetsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The site has no presets yet."));

        Field(cut, "Name").Input("card");
        Field(cut, "Width in pixels").Change("400");
        Field(cut, "Height in pixels").Change("300");
        Field(cut, "Fit").Change("cover");
        cut.Find("form.media-presets-form").Submit();

        cut.WaitForAssertion(() => cut.Find(".media-presets-table tbody").TextContent.ShouldContain("card"));
        Library.Requests.Single(request => request.Method == HttpMethod.Put).Body!.ToJsonString().ShouldBe("""{"width":400,"height":300,"fit":"cover"}""");
        cut.Find(".media-presets-table tbody td").TextContent.ShouldBe("400 × 300, Cover");
        Announcer.Messages.ShouldContain("Preset card added.");

        cut.Find("fluent-button[aria-label='Change card']").Click();
        Field(cut, "Name").HasAttribute("readonly").ShouldBeTrue();
        Field(cut, "Quality").Change("70");
        cut.Find("form.media-presets-form").Submit();
        cut.WaitForAssertion(() => cut.Find(".media-presets-table tbody td").TextContent.ShouldBe("400 × 300, Cover, quality 70"));

        cut.Find("fluent-button[aria-label='Delete card']").Click();
        cut.WaitForAssertion(() => HasButton(cut, "Delete").ShouldBeTrue());
        Button(cut, "Delete").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The site has no presets yet."));
        Library.Presets.ShouldBeEmpty();
        Library.Requests.ShouldContain(request => request.Method == HttpMethod.Delete && request.Path == "media/presets/card");
    }

    [Fact]
    public void AnAddWithTheNameOfAPresetIsRefusedByTheField()
    {
        Library.Presets.Add(new System.Text.Json.Nodes.JsonObject { ["name"] = "card", ["options"] = new System.Text.Json.Nodes.JsonObject { ["width"] = 400 } });
        var cut = Render<MediaPresetsPage>();
        cut.WaitForAssertion(() => cut.Find(".media-presets-table tbody").TextContent.ShouldContain("card"));

        Field(cut, "Name").Input("card");
        Field(cut, "Width in pixels").Change("10");
        cut.Find("form.media-presets-form").Submit();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("A preset named card exists."));
        Field(cut, "Name").GetAttribute("aria-invalid").ShouldBe("true");
        Library.Requests.ShouldNotContain(request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public void ARefusedFieldShowsItsMessageByTheField()
    {
        var cut = Render<MediaPresetsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The site has no presets yet."));
        Library.BeforeAnswer = request => request.Method == HttpMethod.Put
            ? throw new ApiProblemException(new ApiProblem { Status = 400, Errors = [new ApiProblemError { Path = "name", Code = "FCMS1580", Message = "A preset name has lower-case letters." }] })
            : Task.CompletedTask;

        Field(cut, "Name").Input("Card");
        cut.Find("form.media-presets-form").Submit();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("A preset name has lower-case letters."));
        Field(cut, "Name").GetAttribute("aria-invalid").ShouldBe("true");
    }
}
