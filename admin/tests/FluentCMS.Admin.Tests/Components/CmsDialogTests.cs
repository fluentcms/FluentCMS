using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

// Focus moving in, staying in and returning is the browser's modal <dialog> with the library's script; it was checked in a
// browser (#3140). These tests hold what the wrappers add: the name, the modal type and the open state.
public sealed class CmsDialogTests : ComponentTestContext
{
    private const string Show = "Microsoft.FluentUI.Blazor.Components.Dialog.Show";
    private const string Hide = "Microsoft.FluentUI.Blazor.Components.Dialog.Hide";

    [Fact]
    public void TheDialogIsNamedByItsTitleAndIsModal()
    {
        var dialog = Render<CmsDialog>(p => p.Add(x => x.Title, "Discard changes?"));

        var host = dialog.Find("fluent-dialog");
        host.GetAttribute("aria-label").ShouldBe("Discard changes?");
        // The library's "modal" type: a modal <dialog> with role dialog, closed by Escape. "alert" would be an alertdialog.
        host.GetAttribute("type").ShouldBe("modal");
        dialog.Find(".cms-dialog-title").TextContent.ShouldBe("Discard changes?");
    }

    [Fact]
    public void OpeningShowsTheDialogAndClosingHidesIt()
    {
        var dialog = Render<CmsDialog>(p => p.Add(x => x.Title, "Discard changes?"));
        var id = dialog.Find("fluent-dialog").Id;
        JSInterop.Invocations.ShouldNotContain(i => i.Identifier == Show);

        dialog.Render(p => p.Add(x => x.Open, true));
        JSInterop.Invocations.ShouldContain(i => i.Identifier == Show && Equals(i.Arguments[0], id));

        dialog.Render(p => p.Add(x => x.Open, false));
        JSInterop.Invocations.ShouldContain(i => i.Identifier == Hide && Equals(i.Arguments[0], id));
    }

    [Fact]
    public async Task ClosingByEscapeOrOutsideClickReportsOpenFalse()
    {
        bool? open = null;
        var dialog = Render<CmsDialog>(p => p
            .Add(x => x.Title, "Discard changes?")
            .Add(x => x.Open, true)
            .Add(x => x.OpenChanged, v => open = v));
        var host = dialog.Find("fluent-dialog");

        await host.TriggerEventAsync("ondialogtoggle", new DialogToggleEventArgs { Id = host.Id, OldState = "open", NewState = "closed" });

        open.ShouldBe(false);
    }

    // A dialog closed by its parent raises no OpenChanged, and still says once it has closed, after the library gave focus back.
    [Fact]
    public async Task ClosedIsRaisedWhoeverClosedTheDialog()
    {
        var closed = 0;
        bool? open = null;
        var dialog = Render<CmsDialog>(p => p
            .Add(x => x.Title, "Discard changes?")
            .Add(x => x.Open, true)
            .Add(x => x.OpenChanged, v => open = v)
            .Add(x => x.OnClosed, () => closed++));
        var host = dialog.Find("fluent-dialog");

        dialog.Render(p => p.Add(x => x.Open, false));
        await host.TriggerEventAsync("ondialogtoggle", new DialogToggleEventArgs { Id = host.Id, OldState = "open", NewState = "closed" });

        open.ShouldBeNull();
        closed.ShouldBe(1);

        await host.TriggerEventAsync("ondialogtoggle", new DialogToggleEventArgs { Id = "menu", NewState = "closed" });

        closed.ShouldBe(1);
    }

    [Fact]
    public async Task APopoverClosingInsideTheDialogLeavesItOpen()
    {
        bool? open = null;
        var dialog = Render<CmsDialog>(p => p
            .Add(x => x.Title, "Discard changes?")
            .Add(x => x.Open, true)
            .Add(x => x.OpenChanged, v => open = v));

        await dialog.Find("fluent-dialog").TriggerEventAsync("ondialogtoggle", new DialogToggleEventArgs { Id = "menu", NewState = "closed" });

        open.ShouldBeNull();
    }

    [Fact]
    public void TheDialogCanBeOpenedAgainAfterItWasClosed()
    {
        var dialog = Render<CmsDialog>(p => p.Add(x => x.Title, "Discard changes?").Add(x => x.Open, true));
        var host = dialog.Find("fluent-dialog");
        host.TriggerEvent("ondialogtoggle", new DialogToggleEventArgs { Id = host.Id, NewState = "closed" });

        dialog.Render(p => p.Add(x => x.Open, false));
        dialog.Render(p => p.Add(x => x.Open, true));

        JSInterop.Invocations.Count(i => i.Identifier == Show).ShouldBe(2);
    }

    [Fact]
    public void ADismissedDialogStaysClosedWhenTheParentPassesOpenOneWay()
    {
        var dialog = Render<CmsDialog>(p => p.Add(x => x.Title, "Discard changes?").Add(x => x.Open, true));
        var host = dialog.Find("fluent-dialog");
        host.TriggerEvent("ondialogtoggle", new DialogToggleEventArgs { Id = host.Id, NewState = "closed" });

        dialog.Render(p => p.Add(x => x.Open, true));

        JSInterop.Invocations.Count(i => i.Identifier == Show).ShouldBe(1);
    }

    [Fact]
    public void TheDialogKeepsTheLibrarysFocusReturn()
    {
        var dialog = Render<CmsDialog>(p => p.Add(x => x.Title, "Discard changes?"));

        // The library's beforetoggle handler records the element that had focus, so that closing gives it back.
        dialog.Find("fluent-dialog").HasAttribute("blazor:ondialogbeforetoggle").ShouldBeTrue();
    }

    // The close button hides the dialog as Escape does, so the library gives focus back and the toggle reports the close.
    [Fact]
    public async Task TheCloseButtonIsNamedAndClosesTheDialogAsEscapeDoes()
    {
        bool? open = null;
        var dialog = Render<CmsDialog>(p => p
            .Add(x => x.Title, "Discard changes?")
            .Add(x => x.Open, true)
            .Add(x => x.OpenChanged, v => open = v));
        var host = dialog.Find("fluent-dialog");

        var close = host.QuerySelector("fluent-button[aria-label='Close']").ShouldNotBeNull();
        close.GetAttribute("title").ShouldBe("Close");
        await close.ClickAsync(new());
        JSInterop.Invocations.ShouldContain(i => i.Identifier == Hide && Equals(i.Arguments[0], host.Id));
        await host.TriggerEventAsync("ondialogtoggle", new DialogToggleEventArgs { Id = host.Id, OldState = "open", NewState = "closed" });

        open.ShouldBe(false);
    }

    // The library focuses the element with autofocus as the dialog opens; without it the close button, first in the document,
    // would take focus, so every enabled wrapped control of the body and the actions carries it.
    [Fact]
    public void ADialogOpensOnItsFirstFieldAndADrawerOnItsFirstControlNotOnTheCloseButton()
    {
        Microsoft.AspNetCore.Components.RenderFragment fields = builder =>
        {
            builder.OpenComponent<CmsTextInput>(0);
            builder.AddComponentParameter(1, nameof(CmsTextInput.Label), "Disabled");
            builder.AddComponentParameter(2, nameof(CmsTextInput.Disabled), true);
            builder.CloseComponent();
            builder.OpenComponent<CmsTextInput>(3);
            builder.AddComponentParameter(4, nameof(CmsTextInput.Label), "Value");
            builder.CloseComponent();
        };
        Microsoft.AspNetCore.Components.RenderFragment apply = builder =>
        {
            builder.OpenComponent<CmsButton>(0);
            builder.AddComponentParameter(1, nameof(CmsButton.ChildContent), (Microsoft.AspNetCore.Components.RenderFragment)(text => text.AddContent(0, "Apply")));
            builder.CloseComponent();
        };
        var dialog = Render<CmsDialog>(p => p.Add(x => x.Title, "Add a filter").Add(x => x.ChildContent, fields).Add(x => x.Actions, apply));
        var drawer = Render<CmsDrawer>(p => p.Add(x => x.Title, "Filters").Add(x => x.Actions, apply));

        // The library focuses the first element with autofocus in the document: the enabled field, before the actions.
        dialog.FindAll("[autofocus]")[0].Id.ShouldBe(dialog.FindAll("input")[1].Id);
        dialog.FindAll("input")[0].HasAttribute("autofocus").ShouldBeFalse();
        dialog.Find("fluent-button[aria-label='Close']").HasAttribute("autofocus").ShouldBeFalse();
        drawer.FindAll("[autofocus]")[0].TextContent.Trim().ShouldBe("Apply");
        drawer.Find("fluent-button[aria-label='Close']").HasAttribute("autofocus").ShouldBeFalse();
    }

    [Fact]
    public void TheDrawerHasTheCloseButtonTooAndADialogMayLeaveItOut()
    {
        var drawer = Render<CmsDrawer>(p => p.Add(x => x.Title, "Filters"));
        var dialog = Render<CmsDialog>(p => p.Add(x => x.Title, "Your new key").Add(x => x.CloseButton, false));

        drawer.FindAll("fluent-drawer fluent-button[aria-label='Close']").Count.ShouldBe(1);
        dialog.FindAll("fluent-dialog fluent-button").ShouldBeEmpty();
    }

    // Removed from the page while open, the dialog raises no close of its own, so the wrapper hides it before its element goes
    // and the library gives focus back to the opener as on any close.
    [Fact]
    public async Task ADialogRemovedWhileOpenIsHiddenSoFocusReturnsAndAClosedOneIsLeftAlone()
    {
        var open = Render<CmsDialog>(p => p.Add(x => x.Title, "Discard changes?").Add(x => x.Open, true));
        var closed = Render<CmsDrawer>(p => p.Add(x => x.Title, "Filters"));
        var (openId, closedId) = (open.Find("fluent-dialog").Id, closed.Find("fluent-drawer").Id);

        await DisposeComponentsAsync();

        JSInterop.Invocations.Count(i => i.Identifier == Hide && Equals(i.Arguments[0], openId)).ShouldBe(1);
        JSInterop.Invocations.ShouldNotContain(i => i.Identifier == Hide && Equals(i.Arguments[0], closedId));
    }

    // A popover inside the dialog, such as a menu, toggles as the user works in it; the dialog's subtree is not drawn again.
    [Fact]
    public async Task AToggleInsideTheDialogDoesNotRenderItsSubtreeAgain()
    {
        var renders = 0;
        var dialog = Render<CmsDialog>(p => p
            .Add(x => x.Title, "Discard changes?")
            .Add(x => x.Open, true)
            .Add(x => x.ChildContent, builder => builder.AddContent(0, $"Drawn {++renders}")));
        var drawn = renders;

        await dialog.Find("fluent-dialog").TriggerEventAsync("ondialogtoggle", new DialogToggleEventArgs { Id = "menu", NewState = "open" });
        await dialog.Find("fluent-dialog").TriggerEventAsync("ondialogtoggle", new DialogToggleEventArgs { Id = "menu", NewState = "closed" });

        renders.ShouldBe(drawn);
    }

    [Theory]
    [InlineData(CmsDrawerSide.End, "end")]
    [InlineData(CmsDrawerSide.Start, "start")]
    public void TheDrawerIsANamedModalDialogAtTheChosenEdge(CmsDrawerSide side, string position)
    {
        var drawer = Render<CmsDrawer>(p => p.Add(x => x.Title, "Filters").Add(x => x.Side, side));

        var host = drawer.Find("fluent-drawer");
        host.GetAttribute("aria-label").ShouldBe("Filters");
        host.GetAttribute("type").ShouldBe("modal");
        host.GetAttribute("position").ShouldBe(position);
    }

    [Fact]
    public void ClosingTheDrawerReportsOpenFalse()
    {
        bool? open = null;
        var drawer = Render<CmsDrawer>(p => p.Add(x => x.Title, "Filters").Add(x => x.Open, true).Add(x => x.OpenChanged, v => open = v));
        var host = drawer.Find("fluent-drawer");

        host.TriggerEvent("ondialogtoggle", new DialogToggleEventArgs { Id = host.Id, NewState = "closed" });

        open.ShouldBe(false);
    }
}
