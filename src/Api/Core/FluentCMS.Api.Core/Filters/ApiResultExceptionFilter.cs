namespace FluentCMS.Api.Core.Filters;

public class ApiResultExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (!context.ActionDescriptor.IsApiResultType())
            return;

        var securityContext = context.HttpContext.RequestServices.GetRequiredService<ISecurityContext>();

        var exception = context.Exception;

        var apiResult = new ApiResponse
        {
            Duration = (DateTime.UtcNow - securityContext.StartDate).TotalMilliseconds,
            SessionId = securityContext.SessionId,
            TraceId = securityContext.TraceId,
            UniqueId = securityContext.UniqueId,
            StatusCode = 500,
            Success = false,
            Message = context.Exception.ToString()
        };

        apiResult.Errors.Add(new ApiError { Code = "Unknown", Description = exception.Message });

        context.Result = new ObjectResult(apiResult)
        {
            StatusCode = apiResult.StatusCode
        };
    }
}
