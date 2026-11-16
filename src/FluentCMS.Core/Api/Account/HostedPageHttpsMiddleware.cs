using FluentCMS.Core.Api.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.Api.Account;

// The antiforgery cookie of the hosted pages is Secure, so a page asked for over plain http could not render or check its
// form and failed with a 500. It is refused before it runs, with a page that names the cause. It is not redirected: a
// proxy the host does not trust to forward the scheme sends every request as http, so a redirect to https would loop. A
// request a known proxy forwarded as https is https by now, and a host that relaxes the cookie gets its pages over http.
internal sealed class HostedPageHttpsMiddleware(RequestDelegate next, IOptions<AntiforgeryOptions> antiforgery)
{
    private const string Message =
        "This page is served over https only. Open it at the https address of the site. Behind a proxy that ends https, the host must trust the proxy's forwarded scheme (FluentCms:ReverseProxy:ForwardedHeaders).";

    public Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.IsHttps
            && antiforgery.Value.Cookie.SecurePolicy == CookieSecurePolicy.Always
            && context.GetEndpoint()?.Metadata.GetMetadata<PageActionDescriptor>()?.AreaName == HostedPages.Area)
        {
            var problem = ProblemMapper.Known(ApiErrorCodes.HttpsRequired, "The hosted pages are served over https only.");
            return ProblemDetailsExceptionHandler.WritePageAsync(context, problem, Message, context.RequestAborted);
        }

        return next(context);
    }
}
