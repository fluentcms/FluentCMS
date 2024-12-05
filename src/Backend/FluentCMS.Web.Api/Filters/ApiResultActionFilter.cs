using Microsoft.AspNetCore.Mvc.Filters;

namespace FluentCMS.Web.Api.Filters;

public class ApiResultActionFilter(IApiExecutionContext apiExecutionContext) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Execute the action
        var executedContext = await next();

        // Check if the action returns a value
        if (executedContext.Result is ObjectResult result && context.ActionDescriptor.IsApiResultType())
        {
            // Get the value returned by the action
            var value = result.Value;

            if (value == null)
                return;

            var apiResult = (IApiResult)value;
            apiResult.Duration = (DateTime.UtcNow - apiExecutionContext.StartDate).TotalMilliseconds;
            apiResult.SessionId = apiExecutionContext.SessionId;
            apiResult.TraceId = apiExecutionContext.TraceId;
            apiResult.UniqueId = apiExecutionContext.UniqueId;
            apiResult.Status = 200;
            apiResult.IsSuccess = true;
        }
    }
}
