namespace FluentCMS.Api.Core.Controllers;

[ApiController]
[Produces("application/json")]
[Route("api/[controller]/[action]")]
public abstract class BaseController : ControllerBase
{
    protected IServiceProvider ServiceProvider => ControllerContext.HttpContext.RequestServices;

    protected ISecurityContext SecurityContext => ServiceProvider.GetRequiredService<ISecurityContext>();

    protected IMapper Mapper => ServiceProvider.GetRequiredService<IMapper>();

    protected ApiResponse Success(string? messageCode = null)
    {
        return new ApiResponse
        {
            SessionId = SecurityContext.SessionId,
            UniqueId = SecurityContext.UniqueId,
            StatusCode = 200,
            Duration = (DateTime.UtcNow - SecurityContext.StartDate).TotalMilliseconds,
            Success = true,
            Timestamp = DateTime.UtcNow,
            TraceId = SecurityContext.TraceId,
            MessageCode = messageCode ?? string.Empty
        };
    }

    protected ApiResponse<T> Success<T>(T data, string? messageCode = null)
    {
        return new ApiResponse<T>
        {
            SessionId = SecurityContext.SessionId,
            UniqueId = SecurityContext.UniqueId,
            StatusCode = 200,
            Duration = (DateTime.UtcNow - SecurityContext.StartDate).TotalMilliseconds,
            Success = true,
            Data = data,
            Timestamp = DateTime.UtcNow,
            TraceId = SecurityContext.TraceId,
            MessageCode = messageCode ?? string.Empty
        };
    }

    protected ApiListResponse<T> SuccessList<T>(IEnumerable<T> data, int page, int pageSize, int totalCount)
    {
        var items = data.ToList();
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        var pagination = new PaginationInfo
        {
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            TotalCount = totalCount,
            HasNextPage = page < totalPages,
            HasPreviousPage = page > 1
        };

        return SuccessList(items, pagination);
    }

    protected ApiListResponse<T> SuccessList<T>(IEnumerable<T> data)
    {
        var items = data.ToList();
        var pagination = new PaginationInfo
        {
            Page = 1,
            PageSize = items.Count,
            TotalPages = 1,
            TotalCount = items.Count,
            HasNextPage = false,
            HasPreviousPage = false
        };

        return SuccessList(items, pagination);
    }

    protected ApiListResponse<T> SuccessList<T>(IEnumerable<T> items, PaginationInfo pagination)
    {
        return new ApiListResponse<T>
        {
            SessionId = SecurityContext.SessionId,
            UniqueId = SecurityContext.UniqueId,
            StatusCode = 200,
            Duration = (DateTime.UtcNow - SecurityContext.StartDate).TotalMilliseconds,
            Success = true,
            Data = items,
            Timestamp = DateTime.UtcNow,
            TraceId = SecurityContext.TraceId,
            Pagination = pagination,
        };
    }

    protected ApiResponse BadRequest(string errorMessage, string? messageCode = null)
    {
        return new ApiResponse
        {
            SessionId = SecurityContext.SessionId,
            UniqueId = SecurityContext.UniqueId,
            StatusCode = 400,
            Duration = (DateTime.UtcNow - SecurityContext.StartDate).TotalMilliseconds,
            Success = false,
            Message = errorMessage,
            Timestamp = DateTime.UtcNow,
            MessageCode = messageCode ?? string.Empty,
            TraceId = SecurityContext.TraceId
        };
    }

    protected ApiResponse UnhandledException(Exception exception)
    {
        return new ApiResponse
        {
            SessionId = SecurityContext.SessionId,
            UniqueId = SecurityContext.UniqueId,
            StatusCode = 500,
            Duration = (DateTime.UtcNow - SecurityContext.StartDate).TotalMilliseconds,
            Success = false,
            Message = exception.Message,
            Timestamp = DateTime.UtcNow,
            TraceId = SecurityContext.TraceId
        };
    }

    protected ApiResponse<T> UnhandledException<T>(Exception exception)
    {
        return new ApiResponse<T>
        {
            SessionId = SecurityContext.SessionId,
            UniqueId = SecurityContext.UniqueId,
            StatusCode = 500,
            Duration = (DateTime.UtcNow - SecurityContext.StartDate).TotalMilliseconds,
            Success = false,
            Message = exception.Message,
            Timestamp = DateTime.UtcNow,
            TraceId = SecurityContext.TraceId
        };
    }

    protected ApiResponse Unauthorized(string errorMessage)
    {
        return new ApiResponse
        {
            SessionId = SecurityContext.SessionId,
            UniqueId = SecurityContext.UniqueId,
            StatusCode = 401,
            Duration = (DateTime.UtcNow - SecurityContext.StartDate).TotalMilliseconds,
            Success = false,
            Message = errorMessage,
            Timestamp = DateTime.UtcNow,
            TraceId = SecurityContext.TraceId
        };
    }

    protected ApiResponse<T> Unauthorized<T>(string errorMessage)
    {
        return new ApiResponse<T>
        {
            SessionId = SecurityContext.SessionId,
            UniqueId = SecurityContext.UniqueId,
            StatusCode = 401,
            Duration = (DateTime.UtcNow - SecurityContext.StartDate).TotalMilliseconds,
            Success = false,
            Message = errorMessage,
            Timestamp = DateTime.UtcNow,
            TraceId = SecurityContext.TraceId
        };
    }

}

[Route("api/admin/[controller]/[action]")]
public abstract class BaseAdminController : BaseController
{
}
