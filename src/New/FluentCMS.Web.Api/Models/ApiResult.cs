namespace FluentCMS.Web.Api.Models;

public interface IApiResult
{
    List<AppError> Errors { get; }
    string TraceId { get; set; }
    string SessionId { get; set; }
    string UniqueId { get; set; }
    double Duration { get; set; }
    int Status { get; set; }
}
public interface IApiResult<TData> : IApiResult
{
    TData? Data { get; }
}

public class ApiResult<TData> : IApiResult<TData>
{
    public List<AppError> Errors { get; } = [];
    public string TraceId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
    public double Duration { get; set; }
    public int Status { get; set; }
    public TData? Data { get; }

    public ApiResult()
    {
    }

    public ApiResult(TData data)
    {
        Data = data;
    }
}
