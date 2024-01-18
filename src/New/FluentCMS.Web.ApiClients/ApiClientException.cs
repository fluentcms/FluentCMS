using System.Text.Json;

namespace FluentCMS.Web.ApiClients;

public partial class ApiClientException : Exception
{
    public int StatusCode { get; private set; }
    public string? Response { get; private set; }
    public IReadOnlyDictionary<string, IEnumerable<string>> Headers { get; private set; }
    public ApiExceptionResult? Data { get; private set; } = null;

    public ApiClientException(string message, int statusCode, string? response, IReadOnlyDictionary<string, IEnumerable<string>> headers, Exception? innerException)
        : base(message + "\n\nStatus: " + statusCode + "\nResponse: \n" + ((response == null) ? "(null)" : response.Substring(0, response.Length >= 512 ? 512 : response.Length)), innerException)
    {
        StatusCode = statusCode;
        Response = response;
        Headers = headers;
        if (!string.IsNullOrEmpty(response))
            try
            {
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, };
                Data = JsonSerializer.Deserialize<ApiExceptionResult>(response, options);
            }
            catch (Exception)
            {
            }
    }

    public override string ToString()
    {
        if (Data != null && Data.Errors != null)
            return string.Join(",", Data.Errors.Select(x => $"{x.Code ?? string.Empty}: {x.Description ?? string.Empty}"));

        return string.Format("HTTP Response: \n\n{0}\n\n{1}", Response, base.ToString());
    }
}
