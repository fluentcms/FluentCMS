using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace FluentCMS.Api.Filters;

public static class Helper
{
    public static bool IsApiResultType(this ActionDescriptor? actionDescriptor)
    {
        if (actionDescriptor == null)
            return false;

        if (actionDescriptor.GetType() != typeof(ControllerActionDescriptor))
            return false;

        var descriptor = (ControllerActionDescriptor)actionDescriptor;

        var returnType = descriptor.MethodInfo.ReturnType;

        // Handle Task<> wrapper
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            returnType = returnType.GenericTypeArguments[0]; // Unwrap Task<>
        }

        // Check if returnType implements IApiResult or IApiResult<>
        return typeof(IApiResult).IsAssignableFrom(returnType) ||
               (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(IApiResult<>));
    }
}
