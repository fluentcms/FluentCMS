using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

public sealed class CmsNavigationTests : ComponentTestContext
{
    [Fact]
    public void TheTreeIsNamedAndNestsItsNodes()
    {
        var tree = Render<CmsTree>(p => p
            .Add(x => x.Label, "Folders")
            .Add(x => x.Nodes, [new CmsTreeNode("images", "Images", [new CmsTreeNode("images/2026", "2026")]), new CmsTreeNode("video", "Video")]));

        tree.Find("fluent-tree").GetAttribute("aria-label").ShouldBe("Folders");
        var images = tree.Find("fluent-tree-item#images");
        images.QuerySelector("fluent-tree-item")!.Id.ShouldBe("images/2026");
        tree.FindAll("fluent-tree-item").Count.ShouldBe(3);
    }

    [Fact]
    public void EveryTabHasItsTitleAndControlsItsPanel()
    {
        var tabs = Render<CmsTabs>(p => p
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

        tabs.FindAll("fluent-tab").Select(t => t.TextContent.Trim()).ShouldBe(["Content", "SEO"]);
        tabs.Find("fluent-tab#content").GetAttribute("aria-controls").ShouldBe("content-panel");
        tabs.Find("#content-panel").GetAttribute("role").ShouldBe("tabpanel");
    }

    [Fact]
    public void TheMenuIsOpenedByAButtonWithItsLabelAndListsItsItems()
    {
        // The menu's roles, keyboard and focus return come from the library's script, checked in a browser (#3140).
        var menu = Render<CmsMenu>(p => p
            .Add(x => x.Label, "Actions")
            .Add<CmsMenuItem>(x => x.ChildContent, item => item.AddChildContent("Delete")));

        var trigger = menu.Find("fluent-menu-button");
        trigger.GetAttribute("slot").ShouldBe("trigger");
        trigger.TextContent.Trim().ShouldBe("Actions");
        menu.Find("fluent-menu-list fluent-menu-item").TextContent.Trim().ShouldBe("Delete");
    }

    [Fact]
    public void TheDropZoneIsANativeFileInputThatIsLabelledAndExplained()
    {
        var zone = Render<CmsFileDropZone>(p => p.Add(x => x.Label, "Upload images").Add(x => x.Accept, "image/*").Add(x => x.Multiple, true));

        var input = zone.Find("input[type=file]");
        zone.Find("label").GetAttribute("for").ShouldBe(input.Id);
        zone.Find("label").TextContent.ShouldBe("Upload images");
        input.GetAttribute("accept").ShouldBe("image/*");
        input.HasAttribute("multiple").ShouldBeTrue();
        zone.Find($"#{input.GetAttribute("aria-describedby")}").TextContent.ShouldBe("Drop files here or press Enter to choose them");
    }

    [Fact]
    public void TheDropZoneTakesItsOwnHelp()
    {
        var zone = Render<CmsFileDropZone>(p => p.Add(x => x.Label, "Upload").Add(x => x.Help, "Up to 10 MB."));

        var input = zone.Find("input[type=file]");
        zone.Find($"#{input.GetAttribute("aria-describedby")}").TextContent.ShouldBe("Up to 10 MB.");
    }
}
