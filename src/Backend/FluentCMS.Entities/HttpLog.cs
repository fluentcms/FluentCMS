using System.Collections;

namespace FluentCMS.Entities;

/// <summary>
/// Represents an HTTP log entry that captures detailed information about an HTTP request, response, and any associated exceptions.
/// </summary>
public sealed class HttpLog : Entity
{
    /// <summary>
    /// Gets or sets the HTTP status code of the response.
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Gets or sets the duration of the request processing in milliseconds.
    /// </summary>
    public long Duration { get; set; }

    /// <summary>
    /// Gets or sets the name of the assembly handling the request.
    /// </summary>
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version of the assembly handling the request.
    /// </summary>
    public string AssemblyVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the process ID of the application handling the request.
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// Gets or sets the name of the process handling the request.
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the thread ID where the request is being handled.
    /// </summary>
    public int ThreadId { get; set; }

    /// <summary>
    /// Gets or sets the memory usage in bytes at the time of logging.
    /// </summary>
    public long MemoryUsage { get; set; }

    /// <summary>
    /// Gets or sets the name of the machine handling the request.
    /// </summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the environment (e.g., Development, Production).
    /// </summary>
    public string EnvironmentName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the user running the environment.
    /// </summary>
    public string EnvironmentUserName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the request is authenticated.
    /// </summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// Gets or sets the language of the request.
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the session ID associated with the request.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the start date and time of the request.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Gets or sets the trace ID for distributed tracing.
    /// </summary>
    public string TraceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the unique ID for the request.
    /// </summary>
    public string UniqueId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user ID associated with the request.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the IP address of the user making the request.
    /// </summary>
    public string UserIp { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the username of the user making the request.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the API token key used for authentication.
    /// </summary>
    public string ApiTokenKey { get; set; } = string.Empty;

    #region Request

    /// <summary>
    /// Gets or sets the URL of the request.
    /// </summary>
    public string ReqUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the protocol of the request (e.g., HTTP/1.1).
    /// </summary>
    public string ReqProtocol { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the HTTP method of the request (e.g., GET, POST).
    /// </summary>
    public string ReqMethod { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the scheme of the request (e.g., http, https).
    /// </summary>
    public string ReqScheme { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base path of the request.
    /// </summary>
    public string ReqPathBase { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path of the request.
    /// </summary>
    public string ReqPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the query string of the request.
    /// </summary>
    public string QueryString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content type of the request.
    /// </summary>
    public string ReqContentType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content length of the request body.
    /// </summary>
    public long? ReqContentLength { get; set; }

    /// <summary>
    /// Gets or sets the body of the request.
    /// </summary>
    public string? ReqBody { get; set; }

    /// <summary>
    /// Gets or sets the headers of the request.
    /// </summary>
    public Dictionary<string, string> ReqHeaders { get; set; } = new();

    #endregion

    #region Response

    /// <summary>
    /// Gets or sets the content type of the response.
    /// </summary>
    public string ResContentType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content length of the response body.
    /// </summary>
    public long? ResContentLength { get; set; }

    /// <summary>
    /// Gets or sets the body of the response.
    /// </summary>
    public string? ResBody { get; set; }

    /// <summary>
    /// Gets or sets the headers of the response.
    /// </summary>
    public Dictionary<string, string> ResHeaders { get; set; } = new();

    #endregion

    #region Exception

    /// <summary>
    /// Gets or sets additional data associated with an exception.
    /// </summary>
    public IDictionary? ExData { get; set; }

    /// <summary>
    /// Gets or sets the help link for the exception.
    /// </summary>
    public string? ExHelpLink { get; set; }

    /// <summary>
    /// Gets or sets the HResult code for the exception.
    /// </summary>
    public int? ExHResult { get; set; }

    /// <summary>
    /// Gets or sets the message of the exception.
    /// </summary>
    public string? ExMessage { get; set; }

    /// <summary>
    /// Gets or sets the source of the exception.
    /// </summary>
    public string? ExSource { get; set; }

    /// <summary>
    /// Gets or sets the stack trace of the exception.
    /// </summary>
    public string? ExStackTrace { get; set; }

    #endregion
}
