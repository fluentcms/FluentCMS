using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Admin.Tests;

// The sweep of spec 7: every page of the half under both pseudo-cultures, for a caller who may do everything, with a folder,
// an asset and a preset, their data marked.
public sealed class LocalizationTests : MediaTestContext
{
    public static TheoryData<string, string> Pages() => PseudoSweep.Pages(typeof(MediaPage).Assembly);

    [Theory]
    [MemberData(nameof(Pages))]
    public void EveryTextOfEveryPageComesFromTheResources(string culture, string page)
    {
        this.UsePseudoCulture(culture);
        string Data(string text) => PseudoCulture.Transform(text, culture);
        Admin.Grant(MediaPermissions.Upload, MediaPermissions.Update, MediaPermissions.ManageFolders, MediaPermissions.Delete, "media.presets.manage");
        var photos = Library.AddFolder(Data("Photos"));
        Library.AddAsset(Data("beach.jpg"), photos, mimeType: "image/" + Data("jpeg"), title: Data("Beach"));
        Library.Presets.Add(new System.Text.Json.Nodes.JsonObject { ["name"] = Data("card"), ["options"] = new System.Text.Json.Nodes.JsonObject { ["width"] = 400 } });
        Services.GetRequiredService<NavigationManager>().NavigateTo(page == nameof(MediaPage) ? $"media?folder={photos:D}" : "media/presets");

        IRenderedComponent<IComponent> cut = page switch
        {
            nameof(MediaPage) => Render<MediaPage>(),
            nameof(MediaPresetsPage) => Render<MediaPresetsPage>(),
            _ => throw PseudoSweep.NoSweep(page),
        };

        cut.WaitForAssertion(() => cut.FindAll(page == nameof(MediaPage) ? ".media-tile" : ".media-presets-table tbody tr").Count.ShouldBe(1));
        cut.ShouldBeLocalized();
    }
}
