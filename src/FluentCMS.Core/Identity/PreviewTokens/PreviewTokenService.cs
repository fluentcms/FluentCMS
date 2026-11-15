using FluentCMS.Content;
using FluentCMS.Core.Localization;
using FluentCMS.Schema;
using FluentCMS.Settings;
using FluentCMS.Sites;

namespace FluentCMS.Core.Identity.PreviewTokens;

internal sealed record PreviewTokenRequest(int? Minutes, string? Locale);

internal sealed record IssuedPreviewToken(string Token, DateTimeOffset ExpiresAt, PreviewBinding Binding, string? Url = null, string? PublishedUrl = null);

// Mints a token that reads the draft of one entry in the operation's site, and of one locale when the request names it,
// on the Delivery API. Minting reads that draft as the caller first, so a caller who may not read it gets the 404 the read
// gives and no token. The token is the caller with the preview scope alone: its roles are read again on every request,
// so a user who is deactivated or loses the role stops reading, but signing out does not end a token already handed out.
internal sealed class PreviewTokenService(
    IContentService content,
    IOperationContextAccessor accessor,
    SchemaModel schema,
    PreviewTokenIssuer issuer,
    PreviewTokenOptions options,
    ISettings<PreviewSettings> settings,
    LocaleRegistry locales,
    TimeProvider time)
{
    public async Task<IssuedPreviewToken> MintAsync(string type, Guid id, PreviewTokenRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var operation = accessor.Current ?? throw new InvalidOperationException("A preview token is minted inside an operation.");
        if (operation.Principal.Kind is PrincipalKind.System or PrincipalKind.Anonymous)
        {
            throw new InvalidOperationException("A preview token is minted by a user, a service account or an API key.");
        }

        var lifetime = Lifetime(request.Minutes);
        var found = schema.FindContentType(type) ?? throw new EntryNotFoundException(type, id);
        var locale = string.IsNullOrWhiteSpace(request.Locale) ? null : request.Locale.Trim();
        if (locale is not null && !found.Localized)
        {
            throw new ValidationException([new ValidationError("locale", ValidationErrorCodes.InvalidValue, $"{found.Name} is not localized, so a preview token of it has no locale.")]);
        }

        var read = new GetOptions { Status = QueryStatus.Draft, Locale = locale, Fallback = locale is null ? LocaleFallback.Chain : LocaleFallback.None };
        var draft = await content.GetAsync(found.Name, id, read, cancellationToken) ?? throw new EntryNotFoundException(found.Name, id);

        var binding = new PreviewBinding(operation.Site.Value, found.Name, id, locale);
        // A JWT holds whole seconds, so the answer says the expiry the token has.
        var expires = DateTimeOffset.FromUnixTimeSeconds((time.GetUtcNow() + lifetime).ToUnixTimeSeconds());
        var token = issuer.Create(PreviewTokenIssuer.ClaimsOf(operation.Principal, binding), expires);
        if (await TemplateAsync(found, operation.Site, cancellationToken) is not { } template)
        {
            return new IssuedPreviewToken(token, expires, binding);
        }

        // Filled from what the caller's reads show, which is what the token's own read shows, so a field the caller may not read
        // leaves no URL rather than putting its value in one. The published page is the published row of the locale that gave
        // the draft alone, so a locale that was never published has none.
        var served = found.Localized ? await ServedLocaleAsync(found.Name, id, locale, operation, cancellationToken) : null;
        var published = found.Localized && served is null
            ? null
            : await content.GetAsync(found.Name, id, new GetOptions { Status = QueryStatus.Published, Locale = served, Fallback = LocaleFallback.None }, cancellationToken);
        return new IssuedPreviewToken(token, expires, binding, template.Fill(draft, id, served, token), published is null ? null : template.Fill(published, id, served, token: null));
    }

    // The locale of the row that gave the draft, as the site lists it: the token's, or for a token of no locale the first of the
    // request's chain in which the entry has a row, as a read by id is served. Null for a locale the site does not list.
    private async Task<string?> ServedLocaleAsync(string type, Guid id, string? bound, OperationContext operation, CancellationToken cancellationToken)
    {
        var site = await locales.GetAsync(operation.Site, cancellationToken);
        if (CodeOf(site, bound ?? operation.Locale) is not { } requested)
        {
            return null;
        }

        if (bound is not null)
        {
            return requested;
        }

        foreach (var code in site.Chain(requested).Locales)
        {
            if (await content.GetAsync(type, id, new GetOptions { Status = QueryStatus.Draft, Locale = code, Fallback = LocaleFallback.None }, cancellationToken) is not null)
            {
                return code;
            }
        }

        return null;
    }

    private static string? CodeOf(SiteLocales site, string tag)
    {
        try
        {
            return site.Validate(tag);
        }
        catch (ValidationException)
        {
            return null;
        }
    }

    // The type's template in the site, or in the instance defaults. One that the schema no longer fits, since it was saved, is none.
    private async Task<PreviewUrlTemplate?> TemplateAsync(SchemaType type, SiteId site, CancellationToken cancellationToken)
    {
        var templates = (await settings.GetAsync(site, cancellationToken)).Templates;
        return templates.TryGetValue(type.Name, out var template) ? PreviewUrlTemplate.Parse(template, type, "", []) : null;
    }

    private TimeSpan Lifetime(int? minutes)
    {
        if (minutes is not { } asked)
        {
            return options.DefaultLifetime;
        }

        var most = (int)options.MaximumLifetime.TotalMinutes;
        return asked >= 1 && asked <= most
            ? TimeSpan.FromMinutes(asked)
            : throw new ValidationException([new ValidationError("minutes", PreviewTokenErrorCodes.InvalidLifetime, $"A preview token lasts from 1 to {most} minutes.")]);
    }
}
