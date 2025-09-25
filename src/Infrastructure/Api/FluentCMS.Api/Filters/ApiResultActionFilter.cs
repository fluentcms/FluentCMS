namespace FluentCMS.Api.Filters;

public class ApiResultActionFilter : IAsyncActionFilter
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

            var executionContext = context.HttpContext.RequestServices.GetService<IApplicationExecutionContext>() ??
                throw new InvalidOperationException("ApplicationExecutionContext is not registered in the service collection.");

            var apiResult = (IApiResult)value;
            apiResult.Duration = (DateTime.UtcNow - executionContext.StartDate).TotalMilliseconds;
            apiResult.SessionId = executionContext.SessionId;
            apiResult.TraceId = executionContext.TraceId;
            apiResult.UniqueId = executionContext.UniqueId;
            apiResult.Status = 200;
            apiResult.IsSuccess = true;
        }
    }
}
