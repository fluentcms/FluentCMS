namespace FluentCMS.Api.Filters;

public class ApiResultExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (!context.ActionDescriptor.IsApiResultType())
            return;

        var executionContext = context.HttpContext.RequestServices.GetService<IApplicationExecutionContext>() ??
            throw new InvalidOperationException("ApiExecutionContext is not registered in the service collection.");

        var exception = context.Exception;

        var apiResult = new ApiResult
        {
            Duration = (DateTime.UtcNow - executionContext.StartDate).TotalMilliseconds,
            SessionId = executionContext.SessionId,
            TraceId = executionContext.TraceId,
            UniqueId = executionContext.UniqueId,
            Status = 500,
            IsSuccess = false,
            ExceptionDetails = context.Exception.ToString()
        };

        apiResult.Errors.Add(new ApiError { Code = "Unknown", Description = exception.Message });

        context.Result = new ObjectResult(apiResult)
        {
            StatusCode = apiResult.Status
        };
    }
}
