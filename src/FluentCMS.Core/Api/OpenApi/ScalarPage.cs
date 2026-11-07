using FluentCMS.Core.Api.Common;
using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Scalar.AspNetCore;

namespace FluentCMS.Core.Api.OpenApi;

// The Scalar UI runs script and holds the tokens and keys a user pastes into it, so its policy runs only Scalar's own
// scripts: the bundle and the helper that Scalar.AspNetCore serves from its embedded assets beside the page, and the inline
// script that starts it, by a nonce made per request. Its styles are its own too, though the bundle injects them inline.
// It reads the document from its own origin and sends requests to the document's server, which is the public origin when
// one is set. The CDN fonts, the telemetry and the AI chat would leave the origin, so they are off.
internal static class ScalarPage
{
    public static readonly ContentSecurityPolicyMetadata Policy = new(PolicyFor);

    public static void Configure(ScalarOptions ui, HttpContext context) =>
        ui.WithTitle("FluentCMS API")
            .WithOpenApiRoutePattern(OpenApiRegistration.DocumentUrl(context))
            .WithNonce()
            .DisableDefaultFonts()
            .DisableTelemetry()
            .DisableAgent();

    private static string PolicyFor(HttpContext context)
    {
        var nonce = context.Items.TryGetValue(ScalarOptions.NonceHttpContextItemKey, out var value) && value is string { Length: > 0 } text
            ? $" 'nonce-{text}'"
            : "";
        var server = context.RequestServices.GetRequiredService<PublicOrigin>().Base is { } origin
            ? " " + new Uri(origin).GetLeftPart(UriPartial.Authority)
            : "";
        return $"default-src 'none'; script-src 'self'{nonce}; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; "
            + $"connect-src 'self'{server}; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
    }
}
