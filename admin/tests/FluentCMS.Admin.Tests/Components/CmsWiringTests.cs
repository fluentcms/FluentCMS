using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

// The events the library's scripts raise in a browser, raised here by hand, reach the wrappers' callbacks.
public sealed class CmsWiringTests : ComponentTestContext
{
    [Fact]
    public void CheckingTheCheckboxRaisesValueChanged()
    {
        bool? value = null;
        var checkbox = Render<CmsCheckbox>(p => p.Add(x => x.Label, "Show in navigation").Add(x => x.ValueChanged, v => value = v));

        checkbox.Find("fluent-checkbox").Change(true);

        value.ShouldBe(true);
    }

    [Fact]
    public void TurningTheSwitchOnRaisesValueChanged()
    {
        bool? value = null;
        var toggle = Render<CmsSwitch>(p => p.Add(x => x.Label, "Allow comments").Add(x => x.ValueChanged, v => value = v));

        toggle.Find("fluent-switch").Change(true);

        value.ShouldBe(true);
    }

    [Fact]
    public void ChoosingATabRaisesActiveTabIdChanged()
    {
        string? active = null;
        var tabs = Render<CmsTabs>(p => p
            .Add(x => x.ActiveTabIdChanged, id => active = id)
            .Add(x => x.ChildContent, (RenderFragment)(b =>
            {
                b.OpenComponent<CmsTab>(0);
                b.AddAttribute(1, nameof(CmsTab.Id), "content");
                b.AddAttribute(2, nameof(CmsTab.Title), "Content");
                b.CloseComponent();
                b.OpenComponent<CmsTab>(3);
                b.AddAttribute(4, nameof(CmsTab.Id), "seo");
                b.AddAttribute(5, nameof(CmsTab.Title), "SEO");
                b.CloseComponent();
            })));
        var list = tabs.Find("fluent-tablist");

        list.TriggerEvent("ontabchange", new TabChangeEventArgs { Id = list.Id, ActiveId = "seo" });

        active.ShouldBe("seo");
    }

    [Fact]
    public void SelectingATreeNodeRaisesSelectedIdChanged()
    {
        string? selected = null;
        var tree = Render<CmsTree>(p => p
            .Add(x => x.Label, "Folders")
            .Add(x => x.Nodes, [new CmsTreeNode("images", "Images"), new CmsTreeNode("video", "Video")])
            .Add(x => x.SelectedIdChanged, id => selected = id));

        tree.Find("fluent-tree-item#video").TriggerEvent("ontreechanged", new TreeItemChangedEventArgs { Id = "video", Selected = true });

        selected.ShouldBe("video");
    }

    [Fact]
    public void ChoosingAMenuItemRaisesOnClick()
    {
        var clicks = 0;
        var item = Render<CmsMenuItem>(p => p.AddChildContent("Delete").Add(x => x.OnClick, EventCallback.Factory.Create(this, () => clicks++)));
        var element = item.Find("fluent-menu-item");

        element.TriggerEvent("onmenuitemchange", new MenuItemEventArgs { Id = element.Id });

        clicks.ShouldBe(1);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task ChoosingFilesRaisesOnFilesSelected(bool multiple, int expected)
    {
        IReadOnlyList<IBrowserFile>? files = null;
        var zone = Render<CmsFileDropZone>(p => p
            .Add(x => x.Label, "Upload")
            .Add(x => x.Multiple, multiple)
            .Add(x => x.OnFilesSelected, f => files = f));

        await zone.InvokeAsync(() => zone.FindComponent<InputFile>().Instance.OnChange.InvokeAsync(
            new InputFileChangeEventArgs(multiple ? [new FakeFile("a.png"), new FakeFile("b.png")] : [new FakeFile("a.png")])));

        files.ShouldNotBeNull().Count.ShouldBe(expected);
        files[0].Name.ShouldBe("a.png");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACancelledChooserRaisesNoFiles(bool multiple)
    {
        IReadOnlyList<IBrowserFile>? files = null;
        var zone = Render<CmsFileDropZone>(p => p.Add(x => x.Label, "Upload").Add(x => x.Multiple, multiple).Add(x => x.OnFilesSelected, f => files = f));

        await zone.InvokeAsync(() => zone.FindComponent<InputFile>().Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([])));

        files.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Saved \"a < b\"")]
    [InlineData("Published <b>x</b>")]
    [InlineData("R&D &copy; 2026")]
    public void AToastShowsItsMessageAsText(string message)
    {
        var root = Render<CmsThemeRoot>();
        var toasts = Services.GetRequiredService<ICmsToastService>();

        _ = root.InvokeAsync(() => toasts.ShowAsync(message));

        root.WaitForAssertion(() => root.Find("fluent-toast-b [slot=title]").TextContent.Trim().ShouldBe(message));
        root.FindAll("fluent-toast-b b").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(CmsToastIntent.Info, "info", "polite")]
    [InlineData(CmsToastIntent.Success, "success", "polite")]
    [InlineData(CmsToastIntent.Warning, "warning", "polite")]
    [InlineData(CmsToastIntent.Error, "error", "assertive")]
    public void AToastHasTheIntentAndPolitenessOfItsKind(CmsToastIntent intent, string attribute, string politeness)
    {
        var root = Render<CmsThemeRoot>();
        var toasts = Services.GetRequiredService<ICmsToastService>();

        _ = root.InvokeAsync(() => toasts.ShowAsync("Saved.", intent));

        root.WaitForAssertion(() => root.Find("fluent-toast-b").GetAttribute("intent").ShouldBe(attribute));
        root.Find("fluent-toast-b").GetAttribute("politeness").ShouldBe(politeness);
    }

    [Fact]
    public void AToastCanBeDismissedByANamedControl()
    {
        var root = Render<CmsThemeRoot>();
        var toasts = Services.GetRequiredService<ICmsToastService>();

        _ = root.InvokeAsync(() => toasts.ShowAsync("Saved."));

        root.WaitForAssertion(() => root.Find("fluent-toast-b [slot=action]").TextContent.Trim().ShouldBe("Close"));
    }

    private sealed class FakeFile(string name) : IBrowserFile
    {
        public string Name { get; } = name;

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public long Size => 0;

        public string ContentType => "image/png";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => Stream.Null;
    }
}
