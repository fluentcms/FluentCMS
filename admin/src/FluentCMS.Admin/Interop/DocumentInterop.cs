using Microsoft.JSInterop;

namespace FluentCMS.Admin.Interop;

// The allow-listed user of IJSRuntime for the document (spec 3.1): its lang and dir, the branding's custom properties, and
// the return of focus to an opener.
internal sealed class DocumentInterop(IJSRuntime js) : IAsyncDisposable
{
    public const string ModulePath = "./_content/FluentCMS.Admin/document.js";

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

    // The handle is opaque to the caller, which gives it back to ReturnFocusAsync.
    public async ValueTask<IJSObjectReference> RememberFocusAsync()
    {
        var document = await Module();
        return await document.InvokeAsync<IJSObjectReference>("rememberFocus");
    }

    public async ValueTask ReturnFocusAsync(IJSObjectReference opener)
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

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            await (await module).DisposeAsync();
        }
    }

    private Task<IJSObjectReference> Module() => module ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();
}
