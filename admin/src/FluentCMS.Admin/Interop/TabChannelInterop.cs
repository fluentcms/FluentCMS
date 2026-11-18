using Microsoft.JSInterop;

namespace FluentCMS.Admin.Interop;

// The allow-listed user of IJSRuntime for the BroadcastChannel of the admin's tabs (spec 3.1), which carries the tab ids of
// the sessions and nothing else.
internal sealed class TabChannelInterop(IJSRuntime js) : ITabChannel, IAsyncDisposable
{
    public const string ModulePath = "./_content/FluentCMS.Admin/tabs.js";

    // How long a restore waits for another tab to answer; a reload, whose earlier page is gone, waits it in full.
    public const int AnswerWait = 250;

    private Task<IJSObjectReference>? module;

    public async ValueTask<bool> IsHeldElsewhereAsync(string tabId)
    {
        var tabs = await Module();
        return await tabs.InvokeAsync<bool>("isHeldElsewhere", tabId, AnswerWait);
    }

    public async ValueTask HoldAsync(string tabId)
    {
        var tabs = await Module();
        await tabs.InvokeVoidAsync("hold", tabId);
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
