using Bunit;
using Bunit.TestDoubles;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

// The secret shown once (AQ23): hidden until shown, copied on request through the clipboard alone, closed only by Done, and
// held by nothing once the binding has dropped it.
public sealed class CmsSecretDialogTests : ComponentTestContext
{
    private const string Secret = "fcms_dk_0123456789abcdefghijklmnopqrstuv";
    private const string WriteText = "navigator.clipboard.writeText";
    private const string Show = "Microsoft.FluentUI.Blazor.Components.Dialog.Show";
    private const string Hide = "Microsoft.FluentUI.Blazor.Components.Dialog.Hide";
    private const string Focus = "Blazor._internal.domWrapper.focus";

    private string? bound = Secret;
    private int closed;

    [Fact]
    public void TheSecretIsHiddenUntilTheUserShowsIt()
    {
        var dialog = RenderBound();

        JSInterop.Invocations.ShouldContain(invocation => invocation.Identifier == Show);
        dialog.Markup.ShouldNotContain(Secret);
        dialog.Find(".cms-secret-value").TextContent.ShouldContain(Text("SecretHidden"));
        // A group named by the label: the code element itself may not be named (WAI-ARIA 1.2).
        dialog.Find(".cms-secret[role=group]").GetAttribute("aria-labelledby").ShouldBe(dialog.Find(".cms-secret-label").Id);
        dialog.Find(".cms-secret-value").HasAttribute("aria-labelledby").ShouldBeFalse();

        Button(dialog, "SecretShow").Click();

        dialog.Find(".cms-secret-value").TextContent.ShouldBe(Secret);

        Button(dialog, "SecretHide").Click();

        dialog.Markup.ShouldNotContain(Secret);
    }

    [Fact]
    public void CopyWritesTheSecretToTheClipboardAndSaysSo()
    {
        JSInterop.SetupVoid(WriteText, Secret).SetVoidResult();
        var dialog = RenderBound();

        Button(dialog, "SecretCopy").Click();

        dialog.WaitForAssertion(() => dialog.Find("[role=status]").TextContent.ShouldBe(Text("SecretCopied")));
        dialog.Markup.ShouldNotContain(Secret);
        // The clipboard is the only script the secret reaches: no storage, no other call.
        JSInterop.Invocations.Where(invocation => invocation.Arguments.Any(argument => Equals(argument, Secret)))
            .Select(invocation => invocation.Identifier).ShouldBe([WriteText]);
    }

    [Fact]
    public void ACopyTheBrowserRefusesTellsTheUserToCopyItThemselves()
    {
        JSInterop.SetupVoid(WriteText, _ => true).SetException(new JSException("Write permission denied."));
        var dialog = RenderBound();

        Button(dialog, "SecretCopy").Click();

        dialog.WaitForAssertion(() => dialog.Find("[role=status]").TextContent.ShouldBe(Text("SecretNotCopied")));
    }

    [Fact]
    public void DoneDropsTheSecretAndNothingOfTheDialogHoldsIt()
    {
        JSInterop.SetupVoid(WriteText, Secret).SetVoidResult();
        var dialog = RenderBound();
        Button(dialog, "SecretShow").Click();
        Button(dialog, "SecretCopy").Click();
        dialog.WaitForAssertion(() => dialog.Find("[role=status]").TextContent.ShouldBe(Text("SecretCopied")));

        Button(dialog, "SecretDone").Click();
        bound.ShouldBeNull();
        dialog.Render(parameters => parameters.Add(item => item.Secret, bound));

        JSInterop.Invocations.ShouldContain(invocation => invocation.Identifier == Hide);
        dialog.Markup.ShouldNotContain(Secret);
        dialog.Find("[role=status]").TextContent.ShouldBeEmpty();
        Held(dialog.Instance).ShouldBeEmpty();

        // Shown again for a new secret, it starts hidden.
        dialog.Render(parameters => parameters.Add(item => item.Secret, "fcms_dk_another0123456789abcdefghij"));
        dialog.Markup.ShouldNotContain("fcms_dk_another");
    }

    // A secret closed by mistake cannot be read again, so Escape or a click outside shows the dialog again.
    [Fact]
    public async Task EscapeDoesNotCloseIt()
    {
        var dialog = RenderBound();

        await ClosedByTheBrowserAsync(dialog);

        dialog.WaitForAssertion(() => JSInterop.Invocations.Count(invocation => invocation.Identifier == Show).ShouldBe(2));
        bound.ShouldBe(Secret);
        closed.ShouldBe(0);
    }

    // A secret passed one way would give a dialog that Done never closes, so the binding is asked for before any secret comes.
    [Fact]
    public void ASecretWithoutItsBindingIsRefused()
    {
        Should.Throw<InvalidOperationException>(() => Render<CmsSecretDialog>(p => p.Add(x => x.Title, "New key").Add(x => x.Label, "Secret")))
            .Message.ShouldContain("SecretChanged");
        Should.Throw<InvalidOperationException>(() => Render<CmsSecretDialog>(p => p.Add(x => x.Title, "New key").Add(x => x.Label, "Secret").Add(x => x.Secret, Secret)))
            .Message.ShouldContain("SecretChanged");
    }

    // While it is open, the browser asks before a reload, a close or another site; once it is closed, nothing asks.
    [Fact]
    public void WhileOpenTheBrowserAsksBeforeLeavingThePage()
    {
        var dialog = RenderBound();

        dialog.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeTrue();

        Button(dialog, "SecretDone").Click();
        dialog.Render(parameters => parameters.Add(item => item.Secret, bound));

        dialog.FindComponents<NavigationLock>().ShouldBeEmpty();
    }

    // A route change, Back included, asks with Stay and Leave, since the page that holds the secret goes with it. Leave drops
    // the secret before the route changes, so that a page kept across it does not show it on the new route.
    [Fact]
    public async Task ARouteChangeAsksFirstAndStayKeepsThePage()
    {
        var navigation = Navigation();
        var dialog = RenderBound();
        var start = navigation.Uri;

        _ = dialog.InvokeAsync(() => navigation.NavigateTo("elsewhere"));
        dialog.WaitForAssertion(() => dialog.Find(".cms-secret-leave[role=alert]").TextContent.ShouldContain(Text("SecretLeaveQuestion")));
        Button(dialog, "SecretStay").Click();

        await navigation.EndedAsync(0, NavigationState.Prevented);
        dialog.WaitForAssertion(() => dialog.FindAll(".cms-secret-leave").ShouldBeEmpty());
        navigation.Uri.ShouldBe(start);
        bound.ShouldBe(Secret);

        _ = dialog.InvokeAsync(() => navigation.NavigateTo("elsewhere"));
        dialog.WaitForAssertion(() => dialog.Find(".cms-secret-leave"));
        Button(dialog, "SecretLeave").Click();

        // The record of a navigation is written once the question's handler has returned, so the secret was dropped before.
        await navigation.EndedAsync(1, NavigationState.Succeeded);
        navigation.History.First().Uri.ShouldBe("elsewhere");
        bound.ShouldBeNull();
    }

    // The question takes focus on Stay, and Stay, which goes with it, gives focus back to the dialog's first control.
    [Fact]
    public async Task TheQuestionTakesFocusAndStayGivesItBackToTheDialog()
    {
        var navigation = Navigation();
        var dialog = RenderBound();

        _ = dialog.InvokeAsync(() => navigation.NavigateTo("elsewhere"));
        dialog.WaitForAssertion(() => Focused().ShouldBe(ElementId(dialog, "SecretStay")));
        Button(dialog, "SecretStay").Click();

        await navigation.EndedAsync(0, NavigationState.Prevented);
        dialog.WaitForAssertion(() => Focused().ShouldBe(ElementId(dialog, "SecretShow")));
    }

    // Escape or a click outside while the question is asked answers Stay: the question goes, once, and the dialog shown again
    // has focus.
    [Fact]
    public async Task EscapeDuringTheQuestionAnswersStay()
    {
        var navigation = Navigation();
        var dialog = RenderBound();
        _ = dialog.InvokeAsync(() => navigation.NavigateTo("elsewhere"));
        dialog.WaitForAssertion(() => dialog.Find(".cms-secret-leave"));

        await ClosedByTheBrowserAsync(dialog);

        await navigation.EndedAsync(0, NavigationState.Prevented);
        dialog.FindAll("[role=alert]").ShouldBeEmpty();
        bound.ShouldBe(Secret);
        dialog.WaitForAssertion(() => JSInterop.Invocations.Count(invocation => invocation.Identifier == Show).ShouldBe(2));
        dialog.WaitForAssertion(() => Focused().ShouldBe(ElementId(dialog, "SecretShow")));
    }

    // The caller's own navigation after Done, as to the new key's page, is not asked about.
    [Fact]
    public async Task ANavigationAfterDoneIsNotAskedAbout()
    {
        var navigation = Navigation();
        var dialog = RenderBound();

        Button(dialog, "SecretDone").Click();
        await dialog.InvokeAsync(() => navigation.NavigateTo("elsewhere"));

        await navigation.EndedAsync(0, NavigationState.Succeeded);
        navigation.History.First().Uri.ShouldBe("elsewhere");
        dialog.FindAll(".cms-secret-leave").ShouldBeEmpty();
    }

    // Done released the first secret only: the next one, shown straight from @bind-Secret:after, is guarded again.
    [Fact]
    public async Task ASecretShownStraightAfterDoneIsGuardedToo()
    {
        const string next = "fcms_dk_next0123456789abcdefghijklmno";
        var navigation = Navigation();
        var dialog = Render<CmsSecretDialog>(parameters => parameters
            .Add(item => item.Title, "Copy the new key")
            .Add(item => item.Label, "Secret")
            .Add(item => item.Secret, bound)
            .Add(item => item.SecretChanged, value => bound = value ?? next));

        Button(dialog, "SecretDone").Click();
        dialog.Render(parameters => parameters.Add(item => item.Secret, bound));
        bound.ShouldBe(next);
        _ = dialog.InvokeAsync(() => navigation.NavigateTo("elsewhere"));

        dialog.WaitForAssertion(() => dialog.Find(".cms-secret-leave"));
        dialog.Markup.ShouldNotContain(next);
        Button(dialog, "SecretStay").Click();
        await navigation.EndedAsync(0, NavigationState.Prevented);
    }

    // OnClosed follows Done alone: the dialog also closes when the caller drops the secret itself, as when its page reads
    // another key, and then the caller is not told the user is done.
    [Fact]
    public async Task OnClosedFollowsDoneAlone()
    {
        var dialog = RenderBound();

        dialog.Render(parameters => parameters.Add(item => item.Secret, null));
        await ClosedByTheBrowserAsync(dialog);
        closed.ShouldBe(0);

        bound = Secret;
        dialog.Render(parameters => parameters.Add(item => item.Secret, bound));
        Button(dialog, "SecretDone").Click();
        dialog.Render(parameters => parameters.Add(item => item.Secret, bound));
        await ClosedByTheBrowserAsync(dialog);
        closed.ShouldBe(1);
    }

    // A dialog removed while it asks, as when its page goes, lets the route change go on and holds no lock afterwards.
    [Fact]
    public async Task ADialogRemovedWhileItAsksLetsTheRouteChangeGoOn()
    {
        var navigation = Navigation();
        var dialog = RenderBound();
        _ = dialog.InvokeAsync(() => navigation.NavigateTo("elsewhere"));
        dialog.WaitForAssertion(() => dialog.Find(".cms-secret-leave"));

        await DisposeComponentsAsync();

        await navigation.EndedAsync(0, NavigationState.Succeeded);
        await Renderer.Dispatcher.InvokeAsync(() => navigation.NavigateTo("again"));
        await navigation.EndedAsync(1, NavigationState.Succeeded);
        navigation.History.First().Uri.ShouldBe("again");
    }

    [Fact]
    public void ATitleAndALabelAreRequired()
    {
        Should.Throw<InvalidOperationException>(() => Render<CmsSecretDialog>(p => p.Add(x => x.Label, "Secret"))).Message.ShouldContain("Title");
        Should.Throw<InvalidOperationException>(() => Render<CmsSecretDialog>(p => p.Add(x => x.Title, "New key"))).Message.ShouldContain("Label");
    }

    // The library closes the browser's dialog on Escape, a click outside or a hide, and says so with this event.
    private static Task ClosedByTheBrowserAsync(IRenderedComponent<CmsSecretDialog> dialog)
    {
        var host = dialog.Find("fluent-dialog");
        return host.TriggerEventAsync("ondialogtoggle", new DialogToggleEventArgs { Id = host.Id, OldState = "open", NewState = "closed" });
    }

    private BunitNavigationManager Navigation() => (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<CmsSecretDialog> RenderBound() =>
        Render<CmsSecretDialog>(parameters => parameters
            .Add(item => item.Title, "Copy the new key")
            .Add(item => item.Label, "Secret")
            .Add(item => item.Secret, bound)
            .Add(item => item.SecretChanged, value => bound = value)
            .Add(item => item.OnClosed, () => closed++));

    private string Text(string name) => Services.GetRequiredService<ICmsTextSource>().GetText(name)!;

    private AngleSharp.Dom.IElement Button(IRenderedComponent<CmsSecretDialog> dialog, string name) =>
        dialog.FindAll("fluent-button").Single(button => button.TextContent.Trim() == Text(name));

    // The element of a button as the page has it now, which a focus call names.
    private string ElementId(IRenderedComponent<CmsSecretDialog> dialog, string name) =>
        dialog.FindComponents<FluentButton>().Single(button => button.FindAll("fluent-button") is [var host, ..] && host.TextContent.Trim() == Text(name)).Instance.Element.Id;

    private string? Focused() =>
        JSInterop.Invocations.Where(invocation => invocation.Identifier == Focus).Select(invocation => invocation.Arguments[0]).LastOrDefault() is ElementReference element
            ? element.Id
            : null;

    // The string fields of the component that still hold the secret, its parameter included.
    private static string[] Held(object component) =>
        [.. component.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Where(field => field.FieldType == typeof(string) && field.GetValue(component) is string value && value.Contains(Secret, StringComparison.Ordinal))
            .Select(field => field.Name)];
}
