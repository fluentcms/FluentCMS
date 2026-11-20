using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FluentCMS.Admin.Interop;

// The allow-listed user of IJSRuntime for the preview frame (spec 4.12): the messages its window posts, handed on as the
// origin and the JSON they came with, and the admin's messages to it, which the browser delivers only to the origin named.
internal sealed class PreviewInterop(IJSRuntime js) : IAsyncDisposable
{
    public const string ModulePath = "./_content/FluentCMS.Admin/preview.js";

    private Task<IJSObjectReference>? module;

    // Until the handle is disposed. A message from another window than that of the pane's frame is never handed on.
    public async ValueTask<IAsyncDisposable> ListenAsync(ElementReference pane, Func<string, string, Task> receive)
    {
        ArgumentNullException.ThrowIfNull(receive);
        var preview = await Module();
        var target = DotNetObjectReference.Create(new MessageTarget(receive));
        try
        {
            return new Listener(await preview.InvokeAsync<IJSObjectReference>("listen", pane, target), target);
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }

    public async ValueTask PostAsync(ElementReference pane, string origin, string message)
    {
        var preview = await Module();
        await preview.InvokeVoidAsync("post", pane, origin, message);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            await (await module).DisposeAsync();
        }
    }

    private Task<IJSObjectReference> Module() => module ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    internal sealed class MessageTarget(Func<string, string, Task> receive)
    {
        [JSInvokable]
        public Task ReceiveAsync(string origin, string message) => receive(origin, message);
    }

    private sealed class Listener(IJSObjectReference listener, DotNetObjectReference<MessageTarget> target) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await listener.InvokeVoidAsync("dispose");
                await listener.DisposeAsync();
            }
            catch (Exception exception) when (exception is JSDisconnectedException or JSException)
            {
                // The page or the module is gone, and the listener with it.
            }
            finally
            {
                target.Dispose();
            }
        }
    }
}
