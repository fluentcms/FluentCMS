namespace FluentCMS.Web.Api.Models;

/// <summary>
/// Represents the result of an API call.
/// </summary>
public interface IApiResult
{
    /// <summary>
    /// Gets the list of errors associated with the API result.
    /// </summary>
    List<AppError> Errors { get; }

    /// <summary>
    /// Gets or sets the unique trace identifier for tracking the request.
    /// </summary>
    string TraceId { get; set; }

    /// <summary>
    /// Gets or sets the session identifier associated with the request.
    /// </summary>
    string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier for this specific API result.
    /// </summary>
    string UniqueId { get; set; }

    /// <summary>
    /// Gets or sets the duration of the request in milliseconds.
    /// </summary>
    double Duration { get; set; }

    /// <summary>
    /// Gets or sets the HTTP status code of the API result.
    /// </summary>
    int Status { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the API call was successful.
    /// </summary>
    bool IsSuccess { get; set; }
}

/// <summary>
/// Represents the result of an API call with a specific data type.
/// </summary>
/// <typeparam name="TData">The type of the data returned by the API call.</typeparam>
public interface IApiResult<TData> : IApiResult
{
    /// <summary>
    /// Gets the data returned by the API call.
    /// </summary>
    TData? Data { get; }
}

/// <summary>
/// Provides a concrete implementation of the <see cref="IApiResult{TData}"/> interface.
/// </summary>
/// <typeparam name="TData">The type of the data returned by the API call.</typeparam>
public class ApiResult<TData> : IApiResult<TData>
{
    /// <summary>
    /// Gets the list of errors associated with the API result.
    /// </summary>
    public List<AppError> Errors { get; } = [];

    /// <inheritdoc />
    public string TraceId { get; set; } = string.Empty;

    /// <inheritdoc />
    public string SessionId { get; set; } = string.Empty;

    /// <inheritdoc />
    public string UniqueId { get; set; } = string.Empty;

    /// <inheritdoc />
    public double Duration { get; set; }

    /// <inheritdoc />
    public int Status { get; set; }

    /// <summary>
    /// Gets the data returned by the API call.
    /// </summary>
    public TData? Data { get; }

    /// <inheritdoc />
    public bool IsSuccess { get; set; } = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiResult{TData}"/> class.
    /// </summary>
    public ApiResult()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiResult{TData}"/> class with the specified data.
    /// </summary>
    /// <param name="data">The data returned by the API call.</param>
    public ApiResult(TData data)
    {
        Data = data;
    }
}
