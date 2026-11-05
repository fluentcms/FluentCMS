using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Sites;
using FluentCMS.Localization;
using FluentCMS.Sites;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// What the bridge resolved for one request. Locale is what the client asked for and may be "all"; the operation holds
// one locale, the site's default in that case.
internal sealed record ApiRequest(OperationContext Operation, string Locale)
{
    public static ApiRequest Of(HttpContext httpContext) =>
        httpContext.Features.Get<ApiRequest>() ?? throw new InvalidOperationException("The endpoint does not run through the FluentCMS request bridge.");
}

// Turns an HTTP request into the OperationContext of spec 2.2, for every API: the principal from the authentication
// handler, the site (spec 4.6, with a key's binding first) and the locale.
internal sealed class RequestBridge(ISiteResolver sites, ILocaleResolver locales, TimeProvider time)
{
    public const string SiteHeader = "X-Cms-Site";
    public const string LocaleHeader = "X-Cms-Locale";
    public const string SiteParameter = "site";
    public const string LocaleParameter = "locale";

    public async Task<ApiRequest> ResolveAsync(HttpContext httpContext, Origin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var request = httpContext.Request;
        var principal = RequestPrincipal.From(await RequestAuthentication.UserAsync(httpContext));
        var site = await SiteAsync(request, principal, cancellationToken);

        var asked = await locales.ResolveAsync(
            new LocaleRequest(site) { Parameter = Single(request, LocaleParameter), Header = request.Headers[LocaleHeader].FirstOrDefault() },
            cancellationToken);
        var locale = asked == ILocaleResolver.All ? await locales.ResolveAsync(new LocaleRequest(site), cancellationToken) : asked;

        var operation = new OperationContext(
            principal,
            origin,
            site,
            locale,
            principal.Kind == PrincipalKind.ApiKey ? principal.Id.ToString() : null,
            System.Diagnostics.Activity.Current?.Id ?? httpContext.TraceIdentifier,
            time.GetUtcNow());
        return new ApiRequest(operation, asked);
    }

    // A key bound to one site reads that site. A key bound to several reads the one the request names, or the default
    // site when it is among them. A named site outside the binding is refused the same way whether it exists or not, so
    // a bound key cannot learn which sites there are. A header and a parameter that disagree are refused, not ranked.
    private async Task<SiteId> SiteAsync(HttpRequest request, Principal principal, CancellationToken cancellationToken)
    {
        var header = Present(request.Headers[SiteHeader].FirstOrDefault());
        var parameter = Present(Single(request, SiteParameter));
        var bound = PrincipalBinding.Of(principal).Sites;

        var fromHeader = header is null ? (SiteId?)null : await NamedAsync(SiteHeader, new SiteRequest { Header = header }, bound, cancellationToken);
        var fromParameter = parameter is null ? (SiteId?)null : await NamedAsync(SiteParameter, new SiteRequest { Parameter = parameter }, bound, cancellationToken);
        if (fromHeader is { } one && fromParameter is { } other && one != other)
        {
            throw Refused(SiteParameter, QueryErrorCodes.InvalidParameter, $"{SiteHeader} and {SiteParameter} name different sites.");
        }

        if ((fromHeader ?? fromParameter) is { } named)
        {
            return named;
        }

        if (bound is null)
        {
            return await sites.ResolveAsync(new SiteRequest { Host = request.Host.Host }, cancellationToken);
        }

        switch (bound.Count)
        {
            case 0:
                throw Refused(SiteParameter, RequestErrorCodes.SiteOutsideBinding, "The key is bound to no site.");
            case 1:
                return await sites.ResolveAsync(new SiteRequest { ApiKeySite = new SiteId(bound.Single()) }, cancellationToken);
        }

        var unnamed = await sites.ResolveAsync(new SiteRequest { Host = request.Host.Host }, cancellationToken);
        return bound.Contains(unnamed.Value)
            ? unnamed
            : throw Refused(SiteParameter, RequestErrorCodes.SiteNotNamed, $"The key is bound to several sites; name one with {SiteHeader} or {SiteParameter}.");
    }

    private async Task<SiteId> NamedAsync(string source, SiteRequest request, IReadOnlySet<Guid>? bound, CancellationToken cancellationToken)
    {
        if (bound is null)
        {
            return await sites.ResolveAsync(request, cancellationToken);
        }

        SiteId? site;
        try
        {
            site = await sites.ResolveAsync(request, cancellationToken);
        }
        catch (ValidationException exception) when (exception.Errors.All(error => error.Code == SiteErrorCodes.SiteNotFound))
        {
            site = null;
        }

        return site is { } found && bound.Contains(found.Value)
            ? found
            : throw Refused(source, RequestErrorCodes.SiteOutsideBinding, "The key is not bound to the site the request names.");
    }

    private static string? Single(HttpRequest request, string key)
    {
        var values = request.Query[key];
        return values.Count switch
        {
            0 => null,
            1 => values[0],
            _ => throw Refused(key, QueryErrorCodes.InvalidParameter, $"'{key}' is given more than once."),
        };
    }

    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ValidationException Refused(string path, string code, string message) => new([new ValidationError(path, code, message)]);
}
