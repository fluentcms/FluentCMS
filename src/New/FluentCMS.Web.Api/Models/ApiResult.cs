namespace FluentCMS.Web.Api.Models;
public interface IApiResult
{
    List<Error> Errors { get; }
    string TraceId { get; set; }
    string SessionId { get; set; }
    string UniqueId { get; set; }
    double Duration { get; set; }
    int Code { get; set; }
}
public interface IApiResult<TData>: IApiResult
{
    TData? Data { get; }
}

public class ApiResult<TData> : IApiResult<TData>
{
    public List<Error> Errors { get; internal set; } = [];
    public string TraceId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
    public double Duration { get; set; }
    public int Code { get; set; }
    public TData? Data { get; }

    public ApiResult() { }
    public ApiResult(TData data)
    {
        Data = data;
    }
}
