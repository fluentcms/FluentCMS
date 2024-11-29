using System.Collections;

namespace FluentCMS.Entities.Logging;

public class HttpException
{
    public IDictionary? Data { get; set; } 
    public string HelpLink { get; set; } = string.Empty;
    public int HResult { get; set; } = 0;
    public string Message { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string StackTrace { get; set; } = string.Empty;
}
