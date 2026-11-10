using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.Identity.PreviewTokens;

internal sealed record PreviewTokenRequest(int? Minutes, string? Locale);

internal sealed record IssuedPreviewToken(string Token, DateTimeOffset ExpiresAt, PreviewBinding Binding);

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
        _ = await content.GetAsync(found.Name, id, read, cancellationToken) ?? throw new EntryNotFoundException(found.Name, id);

        var binding = new PreviewBinding(operation.Site.Value, found.Name, id, locale);
        // A JWT holds whole seconds, so the answer says the expiry the token has.
        var expires = DateTimeOffset.FromUnixTimeSeconds((time.GetUtcNow() + lifetime).ToUnixTimeSeconds());
        return new IssuedPreviewToken(issuer.Create(PreviewTokenIssuer.ClaimsOf(operation.Principal, binding), expires), expires, binding);
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
