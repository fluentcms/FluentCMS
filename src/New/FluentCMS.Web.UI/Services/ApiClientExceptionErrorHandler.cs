using Microsoft.JSInterop;
using System.Text.Json;

namespace FluentCMS.Web.UI.Services;

public class ApiClientExceptionErrorHandler(IJSRuntime jsRuntime) : IErrorHandler<ApiClientException>
{
    public async Task HandleException(Exception ex)
    {
        var typedEx = (ApiClientException)ex;
        var response = typedEx.Response;
        var jsonDocument = JsonDocument.Parse(response);
        var errorsProperty = jsonDocument.RootElement.TryGetProperty("errors", out var errors);
        // todo i18n
        errors.EnumerateArray().Select(x => x.GetString());
        await jsRuntime.InvokeVoidAsync("alert", string.Join("\n", errors));

    }
}
