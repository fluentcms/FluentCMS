namespace FluentCMS.Models;

public class ApiResult<TData>
{
    public TData? Data { get; set; }
    public string? ErrorType { get; set; }
    public ICollection<string> Errors { get; set; } = new List<string>();
    public bool IsSuccess { get => !Errors.Any(); }
}
