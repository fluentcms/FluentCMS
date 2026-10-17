using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class DialogTests
{
    [Fact]
    public void Trigger_opens_the_native_dialog_that_its_title_and_description_label()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Dialog>(dialog => dialog
            .AddChildContent<DialogTrigger>(trigger => trigger.AddChildContent("Edit"))
            .AddChildContent<DialogContent>(content => content
                .AddChildContent<DialogTitle>(title => title.AddChildContent("Edit profile"))
                .AddChildContent<DialogDescription>(description => description.AddChildContent("Make changes."))));

        var dialog = cut.Find("dialog");
        var trigger = cut.Find("button[command=show-modal]");

        Assert.Equal(dialog.Id, trigger.GetAttribute("commandfor"));
        Assert.Equal("dialog", trigger.GetAttribute("aria-haspopup"));
        Assert.Equal(cut.Find("h2").Id, dialog.GetAttribute("aria-labelledby"));
        Assert.Equal(cut.Find("p").Id, dialog.GetAttribute("aria-describedby"));
        Assert.Equal(dialog.Id, cut.Find("button[command=close]").GetAttribute("commandfor"));
        Assert.NotNull(dialog.GetAttribute("data-trap-focus"));
    }

    [Fact]
    public async Task Closing_the_dialog_reaches_the_server()
    {
        using var context = new AdminTestContext();
        var closed = false;

        var cut = context.Render<Dialog>(dialog => dialog
            .AddChildContent<DialogContent>(content => content
                .Add(c => c.OnClose, () => closed = true)
                .AddChildContent<DialogTitle>(title => title.AddChildContent("Title"))));

        await cut.Find("dialog").TriggerEventAsync("onclose", EventArgs.Empty);

        Assert.True(closed);
    }
}
