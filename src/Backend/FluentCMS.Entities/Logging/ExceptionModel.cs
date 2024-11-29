using System.Collections;

namespace FluentCMS.Entities.Logging;

public class ExceptionModel
{
    public IDictionary? Data { get; set; } 
    public string HelpLink { get; set; } = string.Empty;
    public int HResult { get; set; } = 0;
    public string Message { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string StackTrace { get; set; } = string.Empty;

    public ExceptionModel() { }

    public ExceptionModel(Exception exception)
    {
        Data = exception.Data;
        HelpLink = exception.HelpLink ?? string.Empty;
        HResult = exception.HResult;
        Message = exception.Message;
        Source = exception.Source ?? string.Empty;
        StackTrace = exception.StackTrace ?? string.Empty;
    }
}
