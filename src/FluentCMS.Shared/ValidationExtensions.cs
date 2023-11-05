using FluentCMS.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using SharpGrip.FluentValidation.AutoValidation.Mvc.Extensions;
using SharpGrip.FluentValidation.AutoValidation.Mvc.Results;

namespace FluentCMS.Web;
public static class ValidationExtensions
{
    public static IServiceCollection AddRequestValidation(this IServiceCollection services)
    {
        services.AddFluentValidationAutoValidation(configuration =>
        {
            // Replace the default result factory with a custom implementation.
            configuration.OverrideDefaultResultFactoryWith<CustomResultFactory>();
        });

        return services;
    }
}

public class CustomResultFactory : IFluentValidationAutoValidationResultFactory
{
    public IActionResult CreateActionResult(ActionExecutingContext context, ValidationProblemDetails? validationProblemDetails)
    {
        return new BadRequestObjectResult(new ApiResult<object>
        {
            ErrorType = "validation",
            Errors = validationProblemDetails?.Errors.ToDictionary(k => k.Key, k => (object)k.Value)
        });
    }
}
