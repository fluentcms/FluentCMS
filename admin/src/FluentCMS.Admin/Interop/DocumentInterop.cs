using Microsoft.JSInterop;

namespace FluentCMS.Admin.Interop;

// The allow-listed user of IJSRuntime for the document (spec 3.1): its lang and dir, the branding's accent as a custom
// property, the return of focus to an opener, and focus on the page's heading.
internal sealed class DocumentInterop(IJSRuntime js) : IAsyncDisposable
{
    public const string ModulePath = "./_content/FluentCMS.Admin/document.js";

    // The library returns focus 25 ms after a dialog closed (FocusOnPreviousActiveElement in its script); this runs later.
    private const int DialogFocusDelay = 100;

    private Task<IJSObjectReference>? module;

    public async ValueTask SetLanguageAsync(string lang, string dir)
    {
        var document = await Module();
        await document.InvokeVoidAsync("setLanguage", lang, dir);
    }

    // A custom property on the document element, set through the CSS object model rather than a <style> element, which a
    // strict Content-Security-Policy would block.
    public async ValueTask SetPropertyAsync(string name, string value)
    {
        var document = await Module();
        await document.InvokeVoidAsync("setProperty", name, value);
    }

    // Disposing the handle returns focus to the element that had it, once. The JavaScript reference stays inside this type.
    public async ValueTask<IAsyncDisposable> RememberFocusAsync()
    {
        var document = await Module();
        return new FocusOpener(this, await document.InvokeAsync<IJSObjectReference>("rememberFocus"));
    }

    // On a route change, and after a drawer closed on one (spec 4.18). A dialog of the component library gives focus back to
    // its opener on a timer once it has closed, so after a dialog the heading is focused once that timer has run.
    public async ValueTask FocusHeadingAsync(bool afterDialog = false)
    {
        var document = await Module();
        await document.InvokeVoidAsync("focusHeading", afterDialog ? DialogFocusDelay : 0);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            await (await module).DisposeAsync();
        }
    }

    private Task<IJSObjectReference> Module() => module ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    private async ValueTask ReturnFocusAsync(IJSObjectReference opener)
    {
        try
        {
            var document = await Module();
            await document.InvokeVoidAsync("returnFocus", opener);
        }
        finally
        {
            await opener.DisposeAsync();
        }
    }

    private sealed class FocusOpener(DocumentInterop document, IJSObjectReference opener) : IAsyncDisposable
    {
        private bool returned;

        public async ValueTask DisposeAsync()
        {
            if (returned)
            {
                return;
            }

            returned = true;
            await document.ReturnFocusAsync(opener);
        }
    }
}
