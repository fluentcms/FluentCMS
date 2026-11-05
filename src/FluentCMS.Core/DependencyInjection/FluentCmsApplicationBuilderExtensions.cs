using FluentCMS.Core.Api.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.DependencyInjection;

public static class FluentCmsApplicationBuilderExtensions
{
    // The request pipeline of FluentCMS in the order it works in: the exception handler, routing, security headers,
    // CORS, authentication, the rate limiter and the request limits. The rate limiter comes after authentication because
    // a key or principal is what it partitions by. A host adds this once, after UsePathBase and before MapFluentCms,
    // and UseAuthorization after it.
    public static IApplicationBuilder UseFluentCms(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.ApplicationServices.GetService<RateLimitOptions>() is null)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.NotRegistered,
                "UseFluentCms found no FluentCMS services. Call builder.Services.AddFluentCms(...) before building the app.");
        }

        app.UseExceptionHandler();
        app.UseRouting();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseCors();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseMiddleware<RequestLimitsMiddleware>();
        return app;
    }
}
