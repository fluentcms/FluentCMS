using Microsoft.AspNetCore.Mvc.Filters;

namespace FluentCMS.Web.Api.Filters;

public class ApiResultExceptionFilter(IApiExecutionContext apiExecutionContext) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (!context.ActionDescriptor.IsApiResultType())
            return;

        var apiResult = new ApiResult<object>
        {
            Duration = (DateTime.UtcNow - apiExecutionContext.StartDate).TotalMilliseconds,
            SessionId = apiExecutionContext.SessionId,
            TraceId = apiExecutionContext.TraceId,
            UniqueId = apiExecutionContext.UniqueId,
            Status = 500,
            IsSuccess = false,
        };

        var exception = context.Exception;
        switch (exception)
        {
            case AppException appException:
                apiResult.Status = 500;
                apiResult.Errors.AddRange(appException.Errors);
                break;

            case Exception _:
                apiResult.Status = 500;
                apiResult.Errors.Add(new AppError { Code = "Unknown", Description = exception.Message });
                break;
        }

        context.Result = new ObjectResult(apiResult)
        {
            StatusCode = apiResult.Status
        };
    }
}
