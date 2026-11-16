using Microsoft.JSInterop;

namespace FluentCMS.Admin.Interop;

// The allow-listed user of IJSRuntime for the document (spec 3.1): its lang and dir, and focus.
internal sealed class DocumentInterop(IJSRuntime js) : IAsyncDisposable
{
    public const string ModulePath = "./_content/FluentCMS.Admin/document.js";

    private Task<IJSObjectReference>? module;

    public async ValueTask SetLanguageAsync(string lang, string dir)
    {
        var document = await Module();
        await document.InvokeVoidAsync("setLanguage", lang, dir);
    }

    public async ValueTask<IJSObjectReference> RememberFocusAsync()
    {
        var document = await Module();
        return await document.InvokeAsync<IJSObjectReference>("rememberFocus");
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
