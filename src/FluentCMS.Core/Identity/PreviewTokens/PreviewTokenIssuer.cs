using System.Security.Claims;
using FluentCMS.Core.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace FluentCMS.Core.Identity.PreviewTokens;

internal enum PreviewTokenFailure
{
    None,
    Invalid,
    Expired,
}

// Signs preview tokens with the authorization server's key and reads them back. The audience and the typ header are their
// own, so an access token is never read as a preview token, nor a preview token as an access token. Nothing is stored:
// the token is good until it expires, and its lifetime is checked against the TimeProvider with no skew.
internal sealed class PreviewTokenIssuer(IOptionsMonitor<OpenIddictServerOptions> server, TimeProvider time)
{
    public const string Audience = "fluentcms:preview";
    public const string TokenType = "fcms-preview+jwt";

    private static readonly string[] Single = [CmsClaimTypes.Subject, CmsClaimTypes.Name, CmsClaimTypes.Kind, CmsClaimTypes.Scope, CmsClaimTypes.Sites, CmsClaimTypes.Types, CmsClaimTypes.Entry, CmsClaimTypes.Locale];
    private static readonly string[] Several = [CmsClaimTypes.Roles, CmsClaimTypes.AuthenticationMethods];

    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public string Create(IReadOnlyDictionary<string, object> claims, DateTimeOffset expires)
    {
        var options = server.CurrentValue;
        var credentials = options.SigningCredentials.FirstOrDefault(credential => credential.Key is AsymmetricSecurityKey)
            ?? throw new InvalidOperationException("The authorization server has no signing key.");
        var now = time.GetUtcNow().UtcDateTime;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Audience = Audience,
            Issuer = options.Issuer?.AbsoluteUri,
            TokenType = TokenType,
            Claims = claims.ToDictionary(),
            IssuedAt = now,
            NotBefore = now,
            Expires = expires.UtcDateTime,
            SigningCredentials = credentials,
        });
    }

    public async Task<(ClaimsIdentity? Identity, PreviewTokenFailure Failure)> ValidateAsync(string token, string authenticationType)
    {
        var keys = server.CurrentValue.SigningCredentials.Select(credential => credential.Key).ToList();
        var result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidAudience = Audience,
            ValidateIssuer = false,
            ValidTypes = [TokenType],
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerSigningKeys = keys,
            RequireSignedTokens = true,
            ValidateLifetime = false,
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt || jwt.ValidTo == DateTime.MinValue)
        {
            return (null, PreviewTokenFailure.Invalid);
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (jwt.ValidFrom > now)
        {
            return (null, PreviewTokenFailure.Invalid);
        }

        if (jwt.ValidTo <= now)
        {
            return (null, PreviewTokenFailure.Expired);
        }

        // Only the claims a preview principal is made of, so nothing of the JWT itself reaches a row rule.
        var identity = new ClaimsIdentity(authenticationType, CmsClaimTypes.Name, CmsClaimTypes.Roles);
        foreach (var claim in result.ClaimsIdentity.Claims.Where(claim => Single.Contains(claim.Type, StringComparer.Ordinal) || Several.Contains(claim.Type, StringComparer.Ordinal)))
        {
            identity.AddClaim(new Claim(claim.Type, claim.Value));
        }

        return (identity, PreviewTokenFailure.None);
    }

    // What the token says about the principal that minted it, and what it is bound to.
    public static Dictionary<string, object> ClaimsOf(FluentCMS.Content.Principal principal, PreviewBinding binding)
    {
        Dictionary<string, object> claims = new(StringComparer.Ordinal)
        {
            [CmsClaimTypes.Subject] = principal.Id.ToString("D"),
            [CmsClaimTypes.Name] = principal.Name,
            [CmsClaimTypes.Kind] = principal.Kind.ToString(),
            [CmsClaimTypes.Scope] = TokenScopes.Preview,
            [CmsClaimTypes.Sites] = binding.Site.ToString("D"),
            [CmsClaimTypes.Types] = binding.Type,
            [CmsClaimTypes.Entry] = binding.Entry.ToString("D"),
        };

        if (binding.Locale is { } locale)
        {
            claims[CmsClaimTypes.Locale] = locale;
        }

        foreach (var type in Several)
        {
            if (principal.Claim(type)?.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } values)
            {
                claims[type] = values;
            }
        }

        return claims;
    }
}

internal sealed record PreviewBinding(Guid Site, string Type, Guid Entry, string? Locale);
