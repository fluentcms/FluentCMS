using Microsoft.JSInterop;

namespace FluentCMS.Admin.Interop;

// The allow-listed user of IJSRuntime for the tab's sessionStorage (spec 3.1), which the browser's own functions answer, so
// it needs no module. Nothing of the admin writes localStorage.
internal sealed class SessionStorageInterop(IJSRuntime js) : ISessionStorage
{
    public ValueTask<string?> GetAsync(string key) => js.InvokeAsync<string?>("sessionStorage.getItem", key);

    public ValueTask SetAsync(string key, string value) => js.InvokeVoidAsync("sessionStorage.setItem", key, value);

    public ValueTask RemoveAsync(string key) => js.InvokeVoidAsync("sessionStorage.removeItem", key);
}
