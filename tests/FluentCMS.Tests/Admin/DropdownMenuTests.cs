using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class DropdownMenuTests
{
    [Fact]
    public void Trigger_toggles_a_popover_menu_that_names_it()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<DropdownMenu>(menu => menu
            .AddChildContent<DropdownMenuTrigger>(trigger => trigger.AddChildContent("Open"))
            .AddChildContent<DropdownMenuContent>(content => content
                .AddChildContent<DropdownMenuItem>(item => item.AddChildContent("Profile"))
                .AddChildContent<DropdownMenuItem>(item => item.Add(i => i.Disabled, true).AddChildContent("Shortcuts"))));

        var trigger = cut.Find("button");
        var content = cut.Find("[role=menu]");
        var items = cut.FindAll("[role=menuitem]");

        Assert.Equal(content.Id, trigger.GetAttribute("popovertarget"));
        Assert.Equal("menu", trigger.GetAttribute("aria-haspopup"));
        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        Assert.Equal("auto", content.GetAttribute("popover"));
        Assert.Equal(trigger.Id, content.GetAttribute("aria-labelledby"));
        Assert.All(items, item => Assert.Equal("-1", item.GetAttribute("tabindex")));
        Assert.Equal("true", items[1].GetAttribute("aria-disabled"));
    }

    [Fact]
    public async Task Selecting_an_item_invokes_its_callback_unless_it_is_disabled()
    {
        using var context = new AdminTestContext();
        var selected = new List<string>();

        var cut = context.Render<DropdownMenu>(menu => menu
            .AddChildContent<DropdownMenuContent>(content => content
                .AddChildContent<DropdownMenuItem>(item => item.Add(i => i.OnSelect, () => selected.Add("profile")).AddChildContent("Profile"))
                .AddChildContent<DropdownMenuItem>(item => item.Add(i => i.Disabled, true).Add(i => i.OnSelect, () => selected.Add("shortcuts")).AddChildContent("Shortcuts"))));

        var items = cut.FindAll("[role=menuitem]");
        await items[0].ClickAsync(new());
        await items[1].ClickAsync(new());

        Assert.Equal(["profile"], selected);
    }
}
