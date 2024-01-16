using FluentCMS.Web.UI.ErrorHandling;
using Microsoft.JSInterop;
using System.Collections.ObjectModel;
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
        var appErrors = JsonSerializer.Deserialize<Collection<ApiClients.AppError>>(errors.ToString());
        // todo i18n
        await jsRuntime.InvokeVoidAsync("alert", string.Join("\n", appErrors.Select(x => x.Code)));

    }
}
