using Microsoft.AspNetCore.Mvc.Filters;

namespace FluentCMS.Web.Api.Filters;

public class ApiResultExceptionFilter : IExceptionFilter
{
    private readonly IApiExecutionContext _apiExecutionContext;

    public ApiResultExceptionFilter(IApiExecutionContext apiExecutionContext)
    {
        _apiExecutionContext = apiExecutionContext;
    }

    public void OnException(ExceptionContext context)
    {
        if (!context.ActionDescriptor.IsApiResultType())
            return;

        var apiResult = new ApiResult<object>
        {
            Duration = (DateTime.UtcNow - _apiExecutionContext.StartDate).TotalMilliseconds,
            SessionId = _apiExecutionContext.SessionId,
            TraceId = _apiExecutionContext.TraceId,
            UniqueId = _apiExecutionContext.UniqueId,
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
