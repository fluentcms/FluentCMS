using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.PreviewTokens;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Delivery;

// A preview token reads the draft of its entry by id, in its locale when it has one, and nothing else. The engine holds
// it to that entry, type and site whatever reads it; this refuses the rest up front, with a code that says why, rather
// than with the 404 the engine's answer would be, and is where the locale is held, since a fallback reads others.
internal static class PreviewReach
{
    public static void Ensure(HttpContext httpContext, string type, string? id)
    {
        var api = ApiRequest.Of(httpContext);
        var binding = PrincipalBinding.Of(api.Operation.Principal);
        if (binding.Entry is not { } entry)
        {
            return;
        }

        if (id is null)
        {
            throw Outside("id", "A preview token reads its entry by id only.");
        }

        var found = httpContext.RequestServices.GetRequiredService<SchemaModel>().FindContentType(type);
        if (found is null || binding.Types?.Contains(found.Name) != true)
        {
            throw Outside("type", "The preview token is for an entry of another type.");
        }

        if (!Guid.TryParseExact(id, "D", out var asked) || asked != entry)
        {
            throw Outside("id", "The preview token is for another entry.");
        }

        if (api.Operation.Principal.Claim(CmsClaimTypes.Locale) is { } locale && !string.Equals(api.Locale, locale, StringComparison.OrdinalIgnoreCase))
        {
            throw Outside(RequestBridge.LocaleParameter, $"The preview token reads the {locale} locale only.");
        }
    }

    public static void EnsureDraft(HttpContext httpContext, QueryStatus? status)
    {
        if (status != QueryStatus.Draft && PrincipalBinding.Of(ApiRequest.Of(httpContext).Operation.Principal).Entry is not null)
        {
            throw Outside("status", "A preview token reads the draft only; ask for status=draft.");
        }
    }

    private static ValidationException Outside(string path, string message) =>
        new([new ValidationError(path, PreviewTokenErrorCodes.OutsideToken, message)]);
}
