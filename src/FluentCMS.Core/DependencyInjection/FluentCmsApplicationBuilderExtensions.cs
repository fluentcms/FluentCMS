using FluentCMS.Core.Api.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.DependencyInjection;

public static class FluentCmsApplicationBuilderExtensions
{
    // The request pipeline of FluentCMS in the order it works in: what FluentCms:ReverseProxy asks for (forwarded headers,
    // the path base, HSTS and the HTTPS redirect), the exception handler, routing, the request counter, security headers,
    // CORS, the gate that holds FluentCMS routes to the path base, a coarse limit per client address, the request limits,
    // authentication, the rate limiter of the endpoints, the step that drops the body of a HEAD and the output cache. The
    // proxy part comes first so that everything after it sees the scheme and client address the proxy forwarded. The coarse
    // limit and the body cap come before authentication so that a flood or an oversized body is turned away before it
    // reaches a key lookup or the token endpoint, which reads its body there, and the rate limiter after it because a key or
    // principal is what it partitions by. The output cache comes last so that a cached answer is still limited, and so that
    // it knows who is asking. The counter sits right after routing so that it counts what the layers after it turn away, the
    // gate's 404 included. A host adds this once, before MapFluentCms, and UseAuthorization after it; it does not call
    // UsePathBase, UseForwardedHeaders or UseOutputCache itself.
    public static IApplicationBuilder UseFluentCms(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var state = app.ApplicationServices.GetService<ApiPipelineState>() ?? throw new FluentCmsConfigurationException(
            HostErrorCodes.NotRegistered,
            "UseFluentCms found no FluentCMS services. Call builder.Services.AddFluentCms(...) before building the app.");
        state.Configured = true;

        var proxy = app.ApplicationServices.GetRequiredService<ReverseProxyOptions>();
        ReverseProxyPipeline.Use(app, proxy);
        app.UseExceptionHandler();
        app.UseRouting();
        app.UseMiddleware<RequestMetricsMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseCors();
        if (!string.IsNullOrEmpty(proxy.PathBase))
        {
            app.UseMiddleware<PathBaseGateMiddleware>();
        }

        app.UseMiddleware<PreAuthRateLimitMiddleware>();
        app.UseMiddleware<RequestLimitsMiddleware>();
        app.UseAuthentication();
        app.UseMiddleware<ApiAuthenticationMiddleware>();
        app.UseRateLimiter();
        app.UseMiddleware<HeadRequestMiddleware>();
        app.UseOutputCache();
        return app;
    }
}
