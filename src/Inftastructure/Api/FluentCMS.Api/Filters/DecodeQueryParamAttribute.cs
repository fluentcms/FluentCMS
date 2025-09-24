namespace FluentCMS.Api.Filters;

public class DecodeQueryParamAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (var arg in context.ActionArguments)
        {
            if (arg.Value?.GetType() == typeof(string))
                context.ActionArguments[arg.Key] = HttpUtility.UrlDecode(arg.Value as string);
        }

        base.OnActionExecuting(context);
    }
}
