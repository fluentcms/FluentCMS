using System.Text.Json;

namespace FluentCMS.Web.ApiClients;

public partial class ApiClientException : Exception
{
    public int StatusCode { get; private set; }
    public string? Response { get; private set; }
    public IReadOnlyDictionary<string, IEnumerable<string>> Headers { get; private set; }

    public ApiExceptionResult? ApiResult { get; private set; }

    public ApiClientException(string message, int statusCode, string? response, IReadOnlyDictionary<string, IEnumerable<string>> headers, Exception? innerException) : base(message, innerException)
    {
        StatusCode = statusCode;
        Response = response;
        Headers = headers;

        if (!string.IsNullOrEmpty(response))
            try
            {
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                ApiResult = JsonSerializer.Deserialize<ApiExceptionResult>(response, options);
            }
            catch (Exception)
            {
            }
    }
}
