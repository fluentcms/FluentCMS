using FluentCMS.Admin.Interop;
using Microsoft.JSInterop;

namespace FluentCMS.Admin.Accessibility;

internal sealed class FocusService(DocumentInterop document) : IFocusService
{
    public async ValueTask<IAsyncDisposable> RememberOpenerAsync() => new Opener(await document.RememberFocusAsync());

    private sealed class Opener(IJSObjectReference opener) : IAsyncDisposable
    {
        private bool returned;

        public async ValueTask DisposeAsync()
        {
            if (returned)
            {
                return;
            }

            returned = true;
            try
            {
                await opener.InvokeVoidAsync("restore");
            }
            finally
            {
                await opener.DisposeAsync();
            }
        }
    }
}
