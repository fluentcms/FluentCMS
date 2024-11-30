namespace FluentCMS.Web.Api.Middleware;

public class HttpLogConfig
{
    public bool Enable { get; set; } = false;
    public bool EnableRequestBody { get; set; } = false;
    public bool EnableResponseBody { get; set; } = false;
}
