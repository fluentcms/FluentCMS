namespace FluentCMS.Api.Core.Api.Filters;

public class ApiResultValidateModelFilter : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
        {
            // Check if the action returns a value
            if (context.ActionDescriptor.IsApiResultType())
            {
                var securityContext = context.HttpContext.RequestServices.GetRequiredService<ISecurityContext>();

                var apiResult = new ApiResponse
                {
                    Duration = (DateTime.UtcNow - securityContext.StartDate).TotalMilliseconds,
                    SessionId = securityContext.SessionId,
                    TraceId = securityContext.TraceId,
                    UniqueId = securityContext.UniqueId,
                    StatusCode = 400,
                    Success = false,
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
