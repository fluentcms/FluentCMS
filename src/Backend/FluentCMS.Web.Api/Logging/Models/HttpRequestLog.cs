using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace FluentCMS.Web.Api.Logging.Models;

public class HttpRequestLog
{
    public string DisplayUrl { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Scheme { get; set; } = string.Empty;
    public string PathBase { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string QueryString { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long? ContentLength { get; set; }
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = [];

    public HttpRequestLog()
    {
    }

    public HttpRequestLog(HttpRequest request, string requestBody)
    {
        DisplayUrl = request.GetDisplayUrl();
        Protocol = request.Protocol;
        Method = request.Method;
        Scheme = request.Scheme;
        PathBase = request.PathBase;
        Path = request.Path;
        QueryString = request.QueryString.Value ?? string.Empty;
        ContentType = request.ContentType ?? string.Empty;
        ContentLength = request.ContentLength;
        Headers = request.Headers.ToStringDictionary();
        Body = requestBody;
    }
}
