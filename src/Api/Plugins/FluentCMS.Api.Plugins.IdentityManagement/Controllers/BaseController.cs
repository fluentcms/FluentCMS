using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Plugins.IdentityManagement.Controllers;

//[TypeFilter(typeof(ApiTokenAuthorizeFilter))]
public abstract class BaseController : ControllerBase
{
    protected IServiceProvider ServiceProvider => ControllerContext.HttpContext.RequestServices;

    protected ISecurityContext SecurityContext => ServiceProvider.GetRequiredService<ISecurityContext>();

    protected ApiResponse<T> Success<T>(T data)
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
            TraceId = SecurityContext.TraceId
        };
    }

    protected new ApiResponse NoContent()
    {
        return new ApiResponse
        {
            SessionId = SecurityContext.SessionId,
            UniqueId = SecurityContext.UniqueId,
            StatusCode = 204,
            Duration = (DateTime.UtcNow - SecurityContext.StartDate).TotalMilliseconds,
            Success = true,
            Timestamp = DateTime.UtcNow,
            TraceId = SecurityContext.TraceId
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
}
