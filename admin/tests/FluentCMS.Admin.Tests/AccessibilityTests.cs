using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests;

public sealed class AccessibilityTests : AdminTestContext
{
    [Fact]
    public void ARouteChangeMovesFocusToThePageHeading()
    {
        var app = Render<AdminApp>();
        JSInterop.VerifyFocusOnNavigateInvoke(calledTimes: 1);

        Services.GetRequiredService<NavigationManager>().NavigateTo("/not-found");

        app.WaitForAssertion(() =>
        {
            var invocations = JSInterop.VerifyFocusOnNavigateInvoke(calledTimes: 2);
            invocations[^1].Arguments.ShouldBe(["h1"]);
        });
        app.Find("h1").GetAttribute("tabindex").ShouldBe("-1");
    }

    [Fact]
    public void TheSkipLinkMovesFocusToTheMainContent()
    {
        var app = Render<AdminApp>();
        var link = app.Find("a.skip-link");

        link.Click();

        link.GetAttribute("href").ShouldBe("#main");
        var main = app.Find("main#main");
        main.GetAttribute("tabindex").ShouldBe("-1");
        var focused = (ElementReference)JSInterop.VerifyFocusAsyncInvoke().Arguments[0]!;
        focused.Id.ShouldBe(main.GetAttribute("blazor:elementReference"));
    }

    [Fact]
    public void TheSkipLinkComesFirst()
    {
        var app = Render<AdminApp>();

        app.FindAll("a, button, input, main")[0].ClassName.ShouldBe("skip-link");
    }

    [Fact]
    public void AnAnnouncementIsWrittenToTheLiveRegionOfItsPoliteness()
    {
        var app = Render<AdminApp>();
        var announcer = Services.GetRequiredService<IAnnouncer>();

        announcer.Announce("[Saved]");
        announcer.Announce("[Not saved]", AnnouncementPoliteness.Assertive);

        app.WaitForAssertion(() =>
        {
            app.Find("[aria-live=polite]").TextContent.ShouldBe("[Saved]");
            app.Find("[aria-live=assertive]").TextContent.ShouldBe("[Not saved]");
        });
        app.Find("[aria-live=polite]").GetAttribute("role").ShouldBe("status");
        app.Find("[aria-live=assertive]").GetAttribute("role").ShouldBe("alert");
    }

    [Fact]
    public void TheSameAnnouncementTwiceChangesTheRegionSoItIsReadAgain()
    {
        var app = Render<AdminApp>();
        var announcer = Services.GetRequiredService<IAnnouncer>();

        announcer.Announce("[Saved]");
        app.WaitForAssertion(() => app.Find("[aria-live=polite]").TextContent.ShouldBe("[Saved]"));
        announcer.Announce("[Saved]");

        app.WaitForAssertion(() => app.Find("[aria-live=polite]").TextContent.ShouldBe("[Saved] "));
    }

    [Fact]
    public void AnEmptyAnnouncementIsRefused()
    {
        Should.Throw<ArgumentException>(() => Services.GetRequiredService<IAnnouncer>().Announce(" "));
    }

    [Fact]
    public void AClosedDialogReturnsFocusToItsOpener()
    {
        var opener = Document.SetupModule(invocation => invocation.Identifier == "rememberFocus");
        opener.SetupVoid("restore").SetVoidResult();
        var dialog = Render<DialogWithOpener>();

        dialog.Find("#open").Click();
        dialog.Find("[role=dialog]").ShouldNotBeNull();
        opener.Invocations.ShouldBeEmpty();
        dialog.Find("#close").Click();

        dialog.FindAll("[role=dialog]").ShouldBeEmpty();
        opener.VerifyInvoke("restore");
    }

    [Fact]
    public async Task FocusReturnsOnceWhenTheOpenerIsDisposedTwice()
    {
        var opener = Document.SetupModule(invocation => invocation.Identifier == "rememberFocus");
        opener.SetupVoid("restore").SetVoidResult();
        var focus = Services.GetRequiredService<IFocusService>();

        var handle = await focus.RememberOpenerAsync();
        await handle.DisposeAsync();
        await handle.DisposeAsync();

        opener.VerifyInvoke("restore", calledTimes: 1);
    }

    // A dialog as the wrapped set applies the rule: remember the opener when it opens, return focus when it closes.
    private sealed class DialogWithOpener : ComponentBase
    {
        private IAsyncDisposable? opener;

        [Inject]
        private IFocusService Focus { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "id", "open");
            builder.AddAttribute(2, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, OpenAsync));
            builder.CloseElement();
            if (opener is not null)
            {
                builder.OpenElement(3, "div");
                builder.AddAttribute(4, "role", "dialog");
                builder.OpenElement(5, "button");
                builder.AddAttribute(6, "id", "close");
                builder.AddAttribute(7, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, CloseAsync));
                builder.CloseElement();
                builder.CloseElement();
            }
        }

        private async Task OpenAsync() => opener = await Focus.RememberOpenerAsync();

        private async Task CloseAsync()
        {
            var closed = opener!;
            opener = null;
            StateHasChanged();
            await closed.DisposeAsync();
        }
    }
}
