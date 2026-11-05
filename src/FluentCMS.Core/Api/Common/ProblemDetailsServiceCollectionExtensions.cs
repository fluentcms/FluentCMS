using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluentCMS.Core.Api.Common;

internal static class ProblemDetailsServiceCollectionExtensions
{
    // The host puts app.UseExceptionHandler() in the pipeline; this is the handler it finds.
    public static IServiceCollection AddFluentCmsProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionHandler, ProblemDetailsExceptionHandler>());
        return services;
    }
}
