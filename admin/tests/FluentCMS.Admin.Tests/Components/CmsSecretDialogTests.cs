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

    private string? bound = Secret;

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
        var host = dialog.Find("fluent-dialog");

        await host.TriggerEventAsync("ondialogtoggle", new DialogToggleEventArgs { Id = host.Id, OldState = "open", NewState = "closed" });

        dialog.WaitForAssertion(() => JSInterop.Invocations.Count(invocation => invocation.Identifier == Show).ShouldBe(2));
        bound.ShouldBe(Secret);
    }

    // A secret passed one way would give a dialog that Done never closes.
    [Fact]
    public void ASecretWithoutItsBindingIsRefused() =>
        Should.Throw<InvalidOperationException>(() => Render<CmsSecretDialog>(p => p.Add(x => x.Title, "New key").Add(x => x.Label, "Secret").Add(x => x.Secret, Secret)))
            .Message.ShouldContain("SecretChanged");

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

    // A route change, Back included, asks with Stay and Leave, since the page that holds the secret goes with it.
    [Fact]
    public async Task ARouteChangeAsksFirstAndStayKeepsThePage()
    {
        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        var dialog = RenderBound();
        var start = navigation.Uri;

        _ = dialog.InvokeAsync(() => navigation.NavigateTo("elsewhere"));
        dialog.WaitForAssertion(() => dialog.Find(".cms-secret-leave[role=alert]").TextContent.ShouldContain(Text("SecretLeaveQuestion")));
        Button(dialog, "SecretStay").Click();

        await EndedAsync(navigation, 0, NavigationState.Prevented);
        dialog.WaitForAssertion(() => dialog.FindAll(".cms-secret-leave").ShouldBeEmpty());
        navigation.Uri.ShouldBe(start);
        bound.ShouldBe(Secret);

        _ = dialog.InvokeAsync(() => navigation.NavigateTo("elsewhere"));
        dialog.WaitForAssertion(() => dialog.Find(".cms-secret-leave"));
        Button(dialog, "SecretLeave").Click();

        await EndedAsync(navigation, 1, NavigationState.Succeeded);
        navigation.Uri.ShouldEndWith("/elsewhere");
    }

    // The caller's own navigation after Done, as to the new key's page, is not asked about.
    [Fact]
    public async Task ANavigationAfterDoneIsNotAskedAbout()
    {
        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        var dialog = RenderBound();

        Button(dialog, "SecretDone").Click();
        await dialog.InvokeAsync(() => navigation.NavigateTo("elsewhere"));

        await EndedAsync(navigation, 0, NavigationState.Succeeded);
        navigation.Uri.ShouldEndWith("/elsewhere");
        dialog.FindAll(".cms-secret-leave").ShouldBeEmpty();
    }

    [Fact]
    public void ATitleAndALabelAreRequired()
    {
        Should.Throw<InvalidOperationException>(() => Render<CmsSecretDialog>(p => p.Add(x => x.Label, "Secret"))).Message.ShouldContain("Title");
        Should.Throw<InvalidOperationException>(() => Render<CmsSecretDialog>(p => p.Add(x => x.Title, "New key"))).Message.ShouldContain("Label");
    }

    // A navigation ends after the dialog's last render, when its location-changing handler returns, so the wait is on the
    // navigation manager's record of it, which holds each navigation once it has ended, and not on a render.
    private static async Task EndedAsync(BunitNavigationManager navigation, int ended, NavigationState state)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (navigation.History.Count <= ended)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, $"the navigation did not end {state}");
            await Task.Delay(20, Xunit.TestContext.Current.CancellationToken);
        }

        navigation.History.First().State.ShouldBe(state);
    }

    private IRenderedComponent<CmsSecretDialog> RenderBound() =>
        Render<CmsSecretDialog>(parameters => parameters
            .Add(item => item.Title, "Copy the new key")
            .Add(item => item.Label, "Secret")
            .Add(item => item.Secret, bound)
            .Add(item => item.SecretChanged, value => bound = value));

    private string Text(string name) => Services.GetRequiredService<ICmsTextSource>().GetText(name)!;

    private AngleSharp.Dom.IElement Button(IRenderedComponent<CmsSecretDialog> dialog, string name) =>
        dialog.FindAll("fluent-button").Single(button => button.TextContent.Trim() == Text(name));

    // The string fields of the component that still hold the secret, its parameter included.
    private static string[] Held(object component) =>
        [.. component.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Where(field => field.FieldType == typeof(string) && field.GetValue(component) is string value && value.Contains(Secret, StringComparison.Ordinal))
            .Select(field => field.Name)];
}
