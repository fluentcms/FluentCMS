using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

// A plain item and a radio item of the wrapped menu. The library raises a radio item's change again when a sibling is
// checked or its state is set back, which must not choose it (#3178); the change events are raised here by hand.
public sealed class CmsMenuItemTests : ComponentTestContext
{
    [Fact]
    public void APlainItemIsAMenuItemAndActsOnItsChange()
    {
        var clicks = 0;
        var item = Render<CmsMenuItem>(p => p.Add(x => x.OnClick, () => clicks++).AddChildContent("Account"));

        var host = item.Find("fluent-menu-item");
        host.GetAttribute("role").ShouldBe("menuitem");
        host.HasAttribute("aria-checked").ShouldBeFalse();
        Change(host, isChecked: null);

        clicks.ShouldBe(1);
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void ARadioItemIsAMenuItemRadioWithItsState(bool isChecked, string state)
    {
        var item = Render<CmsMenuItem>(p => p.Add(x => x.Checked, isChecked).AddChildContent("Dark theme"));

        var host = item.Find("fluent-menu-item");
        host.GetAttribute("role").ShouldBe("menuitemradio");
        host.GetAttribute("aria-checked").ShouldBe(state);
    }

    [Fact]
    public void ARadioItemActsOnlyWhenTheUserChecksIt()
    {
        var clicks = 0;
        var item = Render<CmsMenuItem>(p => p.Add(x => x.Checked, false).Add(x => x.OnClick, () => clicks++).AddChildContent("Dark theme"));

        Change(item.Find("fluent-menu-item"), isChecked: true);

        clicks.ShouldBe(1);
    }

    [Fact]
    public void ARadioItemSetBackOrCheckedAgainDoesNotAct()
    {
        var clicks = 0;
        var chosen = Render<CmsMenuItem>(p => p.Add(x => x.Checked, true).Add(x => x.OnClick, () => clicks++).AddChildContent("Light theme"));
        var other = Render<CmsMenuItem>(p => p.Add(x => x.Checked, false).Add(x => x.OnClick, () => clicks++).AddChildContent("Dark theme"));

        // The chosen item checked again as its state is drawn, and unchecked as a sibling is checked.
        Change(chosen.Find("fluent-menu-item"), isChecked: true);
        Change(chosen.Find("fluent-menu-item"), isChecked: false);
        // An item that was not chosen, unchecked as its state is set back.
        Change(other.Find("fluent-menu-item"), isChecked: false);

        clicks.ShouldBe(0);
    }

    // The browser's change event, which carries the state a radio item now has and none for a plain item.
    private static void Change(IElement item, bool? isChecked) =>
        item.TriggerEvent("onmenuitemchange", new MenuItemEventArgs { Id = item.Id, Checked = isChecked });
}
