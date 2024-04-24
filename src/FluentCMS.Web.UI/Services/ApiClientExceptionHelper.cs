namespace FluentCMS.Web.UI.Services;

public static class ApiClientExceptionHelper
{
    public static string? GetFirstError(this ApiClientException apiException)
    {

        return apiException!.Data!.Errors!.First().GetErrorString();
    }
    public static IEnumerable<string> GetErrors(this ApiClientException apiException)
    {
        if (apiException.StatusCode == 500 && apiException.Data != null) // this is our serverside errors dto
            return apiException!.Data!.Errors!.Select(x => x.GetErrorString());
        else // this error data is not our DTO
            return [$"Something went wrong <br>ErrorDetails: <pre>{apiException.Message}</pre>"];
    }
    public static string GetErrorString(this ApiClients.AppError error)
    {
        return (string.IsNullOrEmpty(error.Description) ? error.Code : error.Description) ?? throw new Exception("Could not get error message!");
    }
}
