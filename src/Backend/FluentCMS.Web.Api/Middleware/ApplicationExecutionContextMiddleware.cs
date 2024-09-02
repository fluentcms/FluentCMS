using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api.Middleware;

/// <summary>
/// Middleware to inject the ApiExecutionContext into the HttpContext.Items collection.
/// This ensures that the ApiExecutionContext is available and initialized before the request is processed further.
/// </summary>
public class ApplicationExecutionContextMiddleware
{
    // The next middleware in the request pipeline
    private readonly RequestDelegate _next;

    /// <summary>
    /// Constructor to initialize the middleware with the next delegate in the pipeline.
    /// </summary>
    /// <param name="next">The next delegate/middleware in the request pipeline.</param>
    public ApplicationExecutionContextMiddleware(RequestDelegate next)
    {
        _next = next; // Store the next middleware delegate
    }

    /// <summary>
    /// The method that processes each HTTP request.
    /// It injects the ApiExecutionContext into the HttpContext.Items collection,
    /// making it accessible throughout the request's lifecycle.
    /// </summary>
    /// <param name="httpContext">The HttpContext for the current request.</param>
    /// <param name="apiExecutionContext">The ApiExecutionContext instance to be injected.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext httpContext, ApiExecutionContext apiExecutionContext)
    {
        // Add the ApiExecutionContext to the HttpContext.Items collection.
        // This makes it accessible to other components during the request processing.
        httpContext.Items.Add("ApiExecutionContext", apiExecutionContext);

        // Call the next middleware in the pipeline
        await _next(httpContext);
    }
}
