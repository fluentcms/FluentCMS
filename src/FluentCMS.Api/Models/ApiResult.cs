namespace FluentCMS.Api.Models;

/// <summary>
/// Represents a generic API response with a data payload.
/// </summary>
/// <typeparam name="TData">The type of the data payload in the API response.</typeparam>
public interface IApiResult<TData>
{
    /// <summary>
    /// A list of errors encountered during the API request.
    /// </summary>
    List<Error> Errors { get; }

    /// <summary>
    /// A list of debug information, useful for troubleshooting.
    /// </summary>
    List<object> Debug { get; }

    /// <summary>
    /// A unique identifier for the trace associated with the API request.
    /// </summary>
    string TraceId { get; set; }

    /// <summary>
    /// A unique identifier for the session in which the API request was made.
    /// </summary>
    string SessionId { get; set; }

    /// <summary>
    /// The duration of the API request in milliseconds.
    /// </summary>
    double Duration { get; set; }

    /// <summary>
    /// The data payload of the API response.
    /// </summary>
    TData? Data { get; }
}

/// <summary>
/// Represents a non-generic API response.
/// </summary>
public interface IApiResult : IApiResult<object>
{
}

/// <summary>
/// A generic implementation of an API response.
/// </summary>
/// <typeparam name="TData">The type of the data payload in the API response.</typeparam>
public class ApiResult<TData> : IApiResult<TData>
{
    /// <summary>
    /// A list of errors encountered during the API request.
    /// </summary>
    public List<Error> Errors { get; internal set; } = [];

    /// <summary>
    /// A list of debug information, useful for troubleshooting.
    /// </summary>
    public List<object> Debug { get; internal set; } = [];

    /// <summary>
    /// A unique identifier for the trace associated with the API request.
    /// </summary>
    public string TraceId { get; set; } = string.Empty;

    /// <summary>
    /// A unique identifier for the session in which the API request was made.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// The duration of the API request in milliseconds.
    /// </summary>
    public double Duration { get; set; }

    /// <summary>
    /// The data payload of the API response.
    /// </summary>
    public TData? Data { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiResult{TData}"/> class.
    /// </summary>
    public ApiResult() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiResult{TData}"/> class with specified data.
    /// </summary>
    /// <param name="data">The data payload for the API response.</param>
    public ApiResult(TData data)
    {
        Data = data;
    }
}

/// <summary>
/// A non-generic implementation of an API response.
/// </summary>
public class ApiResult : ApiResult<object>, IApiResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiResult"/> class.
    /// </summary>
    public ApiResult() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiResult"/> class with specified data.
    /// </summary>
    /// <param name="data">The data payload for the API response.</param>
    public ApiResult(object data) : base(data)
    {
    }
}
