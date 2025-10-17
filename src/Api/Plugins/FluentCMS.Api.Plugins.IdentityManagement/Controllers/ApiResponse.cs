namespace FluentCMS.Api.Plugins.IdentityManagement.Controllers;

public class ApiResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<ApiError> Errors { get; set; } = [];
    public DateTime Timestamp { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
    public double Duration { get; set; }
}

public class ApiResponse<T> : ApiResponse
{
    public T Data { get; set; } = default!;
}

public class ApiError(string code, string description)
{
    public string Code { get; set; } = code;
    public string Description { get; set; } = description;

    public ApiError(string code) : this(code, string.Empty)
    {
        Code = code;
    }

    public override string ToString()
    {
        return $"{Code}: {Description}";
    }
}

public class ApiListResponse<T> : ApiResponse
{
    public IEnumerable<T> Data { get; set; } = default!;   // Always enumerable
    public PaginationInfo Pagination { get; set; } = default!;  // Always has pagination
}

public class PaginationInfo
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}
