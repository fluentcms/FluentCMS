namespace FluentCMS.Host.Security;

internal static class SecurityHeaders
{
    // Everything from this origin and nothing inline: Blazor's script, the admin's module and the stylesheet are files (SEC-07).
    // Blazor's default reconnect display injects a style element, which this blocks; it still works, unstyled, until the shell brings its own.
    // Browsers also hold the redirect that leaves a form post to form-action, so an external provider's origin is added there.
    public static string ContentSecurityPolicy(IEnumerable<string> formTargets)
    {
        return "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; " +
            $"form-action {string.Join(' ', formTargets.Prepend("'self'"))}; " +
            "script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'";
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, IEnumerable<string> formTargets)
    {
        var policy = ContentSecurityPolicy(formTargets);

        return app.Use((context, next) =>
        {
            // Set as the response starts, so status code pages and every endpoint get them, and the policy replaces
            // the frame-ancestors-only header Blazor adds to interactive pages.
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = policy;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "same-origin";
                return Task.CompletedTask;
            });

            return next(context);
        });
    }
}
