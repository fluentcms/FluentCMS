namespace FluentCMS.Entities.Logging;

public sealed class HttpLog : Entity
{
    public DateTime Time { get; set; }
    public HttpRequestLog? Request { get; set; }
    public HttpResponseLog? Response { get; set; }
    public int StatusCode { get; set; }
    public long Duration { get; set; }
    public string AssemblyName { get; set; } = string.Empty;
    public string AssemblyVersion { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public int ThreadId { get; set; }
    public long MemoryUsage { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public string EnvironmentUserName { get; set; } = string.Empty;
    public IApiExecutionContext? Context { get; set; }
    public ExceptionModel? Exception { get; set; }
}
