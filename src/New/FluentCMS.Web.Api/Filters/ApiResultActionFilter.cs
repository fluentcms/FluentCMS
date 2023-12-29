using Microsoft.AspNetCore.Mvc.Filters;

namespace FluentCMS.Web.Api.Filters;

public class ApiResultActionFilter : IAsyncActionFilter
{
    private readonly ApiExecutionContext _apiExecutionContext;

    public ApiResultActionFilter(ApiExecutionContext apiExecutionContext)
    {
        _apiExecutionContext = apiExecutionContext;
    }
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var start = DateTimeOffset.UtcNow;

        // Execute the action
        var executedContext = await next();

        // Check if the action returns a value
        if (executedContext.Result is ObjectResult result)
        {
            // Get the value returned by the action
            var value = result.Value;

            if (value == null)
                return;

            var resultType = value.GetType();

            if (resultType.IsGenericType && resultType.GetInterfaces().Contains(typeof(IApiResult)))
            {
                var apiResult = (IApiResult)value;
                apiResult.Duration = (DateTimeOffset.UtcNow - start).TotalMilliseconds;
                apiResult.SessionId = _apiExecutionContext.SessionId;
                apiResult.TraceId = _apiExecutionContext.TraceId;
                apiResult.UniqueId = _apiExecutionContext.UniqueId;
                apiResult.Code = 200;
            }

        }
    }
}
