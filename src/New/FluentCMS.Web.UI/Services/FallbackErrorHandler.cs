using Microsoft.JSInterop;

namespace FluentCMS.Web.UI.Services;

public class FallbackErrorHandler(IJSRuntime jsRuntime) : IErrorHandler
{
    public async Task HandleException(Exception ex)
    {
        await jsRuntime.InvokeVoidAsync("alert", ex.Message);
    }
}
