using FluentCMS.Admin.Interop;
using Microsoft.JSInterop;

namespace FluentCMS.Admin.Accessibility;

internal sealed class FocusService(DocumentInterop document) : IFocusService
{
    public async ValueTask<IAsyncDisposable> RememberOpenerAsync() => new Opener(document, await document.RememberFocusAsync());

    private sealed class Opener(DocumentInterop document, IJSObjectReference opener) : IAsyncDisposable
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
