namespace FluentCMS.Server;

public interface IResult<TData>
{
    List<Error> Errors { get; }
    List<object> Debug { get; }
    string TraceId { get; set; }
    string SessionId { get; set; }
    double Duration { get; set; }
    int Code { get; set; }
    TData? Data { get; set; }
    DateTime Timestamp { get; }
}

public interface IResult : IResult<object>
{
}

public class Result<TData> : IResult<TData>
{
    public List<Error> Errors { get; } = [];
    public List<object> Debug { get; } = [];
    public string TraceId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public double Duration { get; set; } = 0;
    public int Code { get; set; } = 200;
    public TData? Data { get; set; }
    public DateTime Timestamp { get; } = DateTime.Now;

    public Result()
    {
        
    }
    public Result(TData data)
    {
        Data = data;
    }
}

public class Result : Result<object>, IResult
{
    public Result()
    {
        
    }

    public Result(object data)
    {
        Data = data;
    }
}