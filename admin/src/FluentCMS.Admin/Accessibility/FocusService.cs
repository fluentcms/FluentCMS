using FluentCMS.Admin.Interop;

namespace FluentCMS.Admin.Accessibility;

internal sealed class FocusService(DocumentInterop document) : IFocusService
{
    public ValueTask<IAsyncDisposable> RememberOpenerAsync() => document.RememberFocusAsync();
}
