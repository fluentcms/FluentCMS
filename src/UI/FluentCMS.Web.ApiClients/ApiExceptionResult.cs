namespace FluentCMS.Web.ApiClients;

public class ApiExceptionResult
{
    public List<AppError>? Errors { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
    public double Duration { get; set; }
    public int Status { get; set; }
}
