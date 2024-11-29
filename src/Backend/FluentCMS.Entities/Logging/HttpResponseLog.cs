namespace FluentCMS.Entities.Logging;

public class HttpResponseLog
{
    public string ContentType { get; set; } = string.Empty;
    public long? ContentLength { get; set; }
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = [];
}
