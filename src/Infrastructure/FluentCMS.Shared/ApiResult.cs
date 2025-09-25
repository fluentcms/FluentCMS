namespace FluentCMS;

public interface IApiResult
{
    List<ApiError> Errors { get; }
    string TraceId { get; set; }
    string SessionId { get; set; }
    string UniqueId { get; set; }
    double Duration { get; set; }
    int Status { get; set; }
    bool IsSuccess { get; set; }
    string ExceptionDetails { get; set; }
}
public interface IApiResult<TData> : IApiResult
{
    TData? Data { get; }
}

public class ApiResult : IApiResult
{
    public List<ApiError> Errors { get; } = [];
    public string TraceId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
    public double Duration { get; set; } = 0;
    public int Status { get; set; } = 200;
    public bool IsSuccess { get; set; } = true;
    public string ExceptionDetails { get; set; } = string.Empty;
}

public class ApiResult<TData> : ApiResult, IApiResult<TData>
{
    public TData? Data { get; set; }

    public ApiResult()
    {
    }

    public ApiResult(TData data)
    {
        Data = data;
    }
}