using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api.Logging.Models;

public class HttpResponseLog
{    public string ContentType { get; set; } = string.Empty;
    public long? ContentLength { get; set; }
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = [];

    public HttpResponseLog() { }

    public HttpResponseLog(HttpResponse response, string responseBody)
    {
        ContentType = response.ContentType ?? string.Empty;
        ContentLength = response.ContentLength;
        Body = responseBody;
        Headers = response.Headers.ToStringDictionary();
    }    
}
