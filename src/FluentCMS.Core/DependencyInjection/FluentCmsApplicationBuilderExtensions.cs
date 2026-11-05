using FluentCMS.Core.Api.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.DependencyInjection;

public static class FluentCmsApplicationBuilderExtensions
{
    // The request pipeline of FluentCMS in the order it works in: what FluentCms:ReverseProxy asks for (forwarded headers,
    // the path base, HSTS and the HTTPS redirect), the exception handler, routing, security headers, CORS, a coarse limit
    // per client address, authentication, the rate limiter of the endpoints and the request limits. The proxy part comes
    // first so that everything after it sees the scheme and client address the proxy forwarded. The coarse limit comes
    // before authentication so that a flood is turned away before it reaches a key lookup or the token endpoint, and the
    // rate limiter after it because a key or principal is what it partitions by. A host adds this once, before
    // MapFluentCms, and UseAuthorization after it; it does not call UsePathBase or UseForwardedHeaders itself.
    public static IApplicationBuilder UseFluentCms(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var state = app.ApplicationServices.GetService<ApiPipelineState>() ?? throw new FluentCmsConfigurationException(
            HostErrorCodes.NotRegistered,
            "UseFluentCms found no FluentCMS services. Call builder.Services.AddFluentCms(...) before building the app.");
        state.Configured = true;

        ReverseProxyPipeline.Use(app, app.ApplicationServices.GetRequiredService<ReverseProxyOptions>());
        app.UseExceptionHandler();
        app.UseRouting();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseCors();
        app.UseMiddleware<PreAuthRateLimitMiddleware>();
        app.UseAuthentication();
        app.UseMiddleware<ApiAuthenticationMiddleware>();
        app.UseRateLimiter();
        app.UseMiddleware<RequestLimitsMiddleware>();
        return app;
    }
}
