using System.Collections;

namespace FluentCMS.Entities.Logging;

public sealed class HttpLog : Entity
{
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
    public bool IsAuthenticated { get; set; }
    public string Language { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string UserIp { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string ApiTokenKey { get; set; } = string.Empty;

    #region Reguest

    public string ReqUrl { get; set; } = string.Empty;
    public string ReqProtocol { get; set; } = string.Empty;
    public string ReqMethod { get; set; } = string.Empty;
    public string ReqScheme { get; set; } = string.Empty;
    public string ReqPathBase { get; set; } = string.Empty;
    public string ReqPath { get; set; } = string.Empty;
    public string QueryString { get; set; } = string.Empty;
    public string ReqContentType { get; set; } = string.Empty;
    public long? ReqContentLength { get; set; }
    public string? ReqBody { get; set; }
    public Dictionary<string, string> ReqHeaders { get; set; } = [];

    #endregion

    #region Response

    public string ResContentType { get; set; } = string.Empty;
    public long? ResContentLength { get; set; }
    public string? ResBody { get; set; }
    public Dictionary<string, string> ResHeaders { get; set; } = [];

    #endregion

    #region Exception

    public IDictionary? ExData { get; set; }
    public string? ExHelpLink { get; set; }
    public int? ExHResult { get; set; }
    public string? ExMessage { get; set; }
    public string? ExSource { get; set; }
    public string? ExStackTrace { get; set; }

    #endregion
}
