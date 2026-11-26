using Microsoft.JSInterop;

namespace FluentCMS.Admin.Interop;

// The allow-listed user of IJSRuntime for the tab's sessionStorage (spec 3.1), which the browser's own functions answer, so
// it needs no module. localStorage is written only by the storage types of the allow-list, each under a key of its own.
// A browser that refuses the storage throws as the script reads window.sessionStorage, which is told apart from a server
// that does not answer.
internal sealed class SessionStorageInterop(IJSRuntime js) : ISessionStorage
{
    public async ValueTask<string?> GetAsync(string key)
    {
        try
        {
            return await js.InvokeAsync<string?>("sessionStorage.getItem", key);
        }
        catch (JSException exception)
        {
            throw new TabStorageRefusedException(exception);
        }
    }

    public async ValueTask SetAsync(string key, string value)
    {
        try
        {
            await js.InvokeVoidAsync("sessionStorage.setItem", key, value);
        }
        catch (JSException exception)
        {
            throw new TabStorageRefusedException(exception);
        }
    }

    public async ValueTask RemoveAsync(string key)
    {
        try
        {
            await js.InvokeVoidAsync("sessionStorage.removeItem", key);
        }
        catch (JSException exception)
        {
            throw new TabStorageRefusedException(exception);
        }
    }
}
