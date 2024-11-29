namespace FluentCMS.Entities.Logging;

public class HttpRequestLog
{
    public string Url { get; set; } = string.Empty;
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
}
