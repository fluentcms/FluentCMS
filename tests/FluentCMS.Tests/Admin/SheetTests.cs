using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class SheetTests
{
    [Fact]
    public void Trigger_opens_a_native_modal_dialog_that_its_title_and_description_label()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Sheet>(sheet => sheet
            .AddChildContent<SheetTrigger>(trigger => trigger.AddChildContent("Open"))
            .AddChildContent<SheetContent>(content => content
                .AddChildContent<SheetHeader>(header => header
                    .AddChildContent<SheetTitle>(title => title.AddChildContent("Edit profile"))
                    .AddChildContent<SheetDescription>(description => description.AddChildContent("Make changes.")))
                .AddChildContent<SheetFooter>(footer => footer.AddChildContent<SheetClose>(close => close.AddChildContent("Cancel")))));

        var dialog = cut.Find("dialog");
        var trigger = cut.Find("[data-slot=sheet-trigger]");

        Assert.Equal(dialog.Id, trigger.GetAttribute("commandfor"));
        Assert.Equal("show-modal", trigger.GetAttribute("command"));
        Assert.Equal("dialog", trigger.GetAttribute("aria-haspopup"));
        Assert.Equal(cut.Find("h2").Id, dialog.GetAttribute("aria-labelledby"));
        Assert.Equal(cut.Find("p").Id, dialog.GetAttribute("aria-describedby"));
        Assert.Equal("dialog", dialog.GetAttribute("data-ui"));
        Assert.True(dialog.HasAttribute("data-trap-focus"));
        Assert.All(cut.FindAll("button[command=close]"), close => Assert.Equal(dialog.Id, close.GetAttribute("commandfor")));
        Assert.Equal("Close", cut.Find("dialog > button[data-slot=sheet-close] .sr-only").TextContent);
        Assert.Contains("open:flex", dialog.ClassList);
        Assert.DoesNotContain("flex", dialog.ClassList);
    }

    [Theory]
    [InlineData(null, "end", new[] { "start-auto", "end-0", "border-s", "h-full", "sm:max-w-sm" })]
    [InlineData("start", "start", new[] { "start-0", "end-auto", "border-e", "h-full" })]
    [InlineData("top", "top", new[] { "top-0", "bottom-auto", "border-b", "w-full" })]
    [InlineData("bottom", "bottom", new[] { "top-auto", "bottom-0", "border-t", "w-full" })]
    public void Each_side_is_pinned_to_its_edge_with_logical_utilities(string? side, string expectedSide, string[] expectedClasses)
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Sheet>(sheet => sheet
            .AddChildContent<SheetContent>(content =>
            {
                if (side is not null)
                {
                    content.Add(c => c.Side, side);
                }
            }));
        var dialog = cut.Find("dialog");

        Assert.Equal(expectedSide, dialog.GetAttribute("data-side"));
        Assert.Subset(dialog.ClassList.ToHashSet(), expectedClasses.ToHashSet());
        Assert.Contains("m-0", dialog.ClassList);
        Assert.DoesNotContain(cut.FindAll("[class]").SelectMany(element => element.ClassList), Utilities.IsPhysical);
    }

    [Fact]
    public async Task Closing_the_sheet_reaches_the_server()
    {
        using var context = new AdminTestContext();
        var closed = false;

        var cut = context.Render<Sheet>(sheet => sheet
            .AddChildContent<SheetContent>(content => content.Add(c => c.OnClose, () => closed = true)));

        await cut.Find("dialog").TriggerEventAsync("onclose", EventArgs.Empty);

        Assert.True(closed);
    }
}
