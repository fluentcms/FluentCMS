using Microsoft.AspNetCore.Mvc.Filters;
using System.Web;

namespace FluentCMS.Api.Filters;

/// <summary>
/// An action filter attribute that decodes URL-encoded query parameters.
/// </summary>
/// <remarks>
/// This filter is applied to action methods or controllers to automatically
/// decode any URL-encoded string parameters passed to the action method.
/// </remarks>
public class DecodeQueryParamAttribute : ActionFilterAttribute
{
    /// <summary>
    /// Called before the action method executes.
    /// </summary>
    /// <param name="context">The action executing context.</param>
    /// <remarks>
    /// Iterates through the action arguments and decodes any string type parameters.
    /// </remarks>
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
