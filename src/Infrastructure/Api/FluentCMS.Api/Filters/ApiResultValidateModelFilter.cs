namespace FluentCMS.Api.Filters;

public class ApiResultValidateModelFilter : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
        {
            // Check if the action returns a value
            if (context.ActionDescriptor.IsApiResultType())
            {
                var executionContext = context.HttpContext.RequestServices.GetService<IApplicationExecutionContext>() ??
                    throw new InvalidOperationException("ApiExecutionContext is not registered in the service collection.");

                var apiResult = new ApiResult
                {
                    Duration = (DateTime.UtcNow - executionContext.StartDate).TotalMilliseconds,
                    SessionId = executionContext.SessionId,
                    TraceId = executionContext.TraceId,
                    UniqueId = executionContext.UniqueId,
                    Status = 400,
                    IsSuccess = false,
                };
                foreach (var item in context.ModelState)
                {
                    var errors = item.Value.Errors;
                    if (errors?.Count > 0)
                    {
                        foreach (var error in errors)
                        {
                            apiResult.Errors.Add(new ApiError { Code = item.Key, Description = error.ErrorMessage });
                        }
                    }
                }

                context.Result = new BadRequestObjectResult(apiResult);
            }
            else
            {
                context.Result = new BadRequestObjectResult(context.ModelState);
            }
        }
    }
}
