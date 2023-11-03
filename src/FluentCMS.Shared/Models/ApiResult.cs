namespace FluentCMS.Models;

public class ApiResult<TData>
{
    public TData? Data { get; set; }
    public string? ErrorType { get; set; }
    public IDictionary<string, object>? Errors { get; set; }
    public bool IsSuccess { get => Errors == null || !Errors.Any(); }
}
