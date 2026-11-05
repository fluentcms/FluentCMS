using FluentCMS.Content;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Common;

// Runs an endpoint inside the OperationContext of its request. A handler builds its whole result before it returns,
// because the context is gone by the time the result is written.
internal sealed class OperationEndpointFilter(Origin origin) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var services = httpContext.RequestServices;
        var request = await services.GetRequiredService<RequestBridge>().ResolveAsync(httpContext, origin, httpContext.RequestAborted);
        httpContext.Features[typeof(ApiRequest)] = request;

        var accessor = services.GetRequiredService<IOperationContextAccessor>();
        var previous = accessor.Current;
        accessor.Current = request.Operation;
        try
        {
            return await next(context);
        }
        finally
        {
            accessor.Current = previous;
        }
    }
}

internal static class OperationEndpointFilterExtensions
{
    public static TBuilder WithFluentCmsOperation<TBuilder>(this TBuilder builder, Origin origin)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new OperationEndpointFilter(origin));
}
