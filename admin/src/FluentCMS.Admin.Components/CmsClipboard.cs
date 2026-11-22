using Microsoft.JSInterop;

namespace FluentCMS.Admin.Components;

// The allow-listed user of IJSRuntime for the clipboard (spec 3.1, 3.3): it writes a text through the browser's own
// navigator.clipboard.writeText, with no module, and reads nothing. It logs nothing either, since what it writes is a secret.
// A browser that refuses, as on an address that is not secure or without the user's permission, answers false.
internal sealed class CmsClipboard(IJSRuntime js)
{
    public const string WriteText = "navigator.clipboard.writeText";

    public async Task<bool> WriteAsync(string text)
    {
        try
        {
            await js.InvokeVoidAsync(WriteText, text);
            return true;
        }
        catch (JSException)
        {
            return false;
        }
    }
}
