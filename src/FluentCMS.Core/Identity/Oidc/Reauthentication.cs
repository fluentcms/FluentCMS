using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;

namespace FluentCMS.Core.Identity.Oidc;

// A sign-in that an authorize request asks for again (prompt=login, a max_age the sign-in is older than, the absolute
// lifetime, a second factor the role now requires) keeps the cookie: another site can send that request, so it must not
// end anything (#3203). The user goes to the login page, and the request comes back with a mark of when it asked and which
// sign-in it found. Only a newer sign-in answers prompt=login and max_age, so the request without one asks again. The mark
// is protected, so another site can make or change none, and it is bound to the request's client_id and code_challenge
// alone, so it answers nothing on a request of another client or with another challenge; it lasts as long as a sign-in may
// take.
internal static class Reauthentication
{
    public const string Parameter = "fcms_reauth";

    private const string Purpose = "FluentCMS.Connect.Reauthentication.v1";

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public static IResult Challenge(HttpContext context, OpenIddictRequest request, List<KeyValuePair<string, StringValues>> parameters, DateTimeOffset signedInAt, DateTimeOffset now, string scheme)
    {
        var mark = Protector(context).Protect(string.Join('|', request.ClientId, request.CodeChallenge, Text(now), Text(signedInAt)));
        var marked = parameters.Where(parameter => parameter.Key != Parameter).Append(KeyValuePair.Create(Parameter, new StringValues(mark)));
        return Results.Challenge(new AuthenticationProperties { RedirectUri = context.Request.PathBase + context.Request.Path + QueryString.Create(marked) }, [scheme]);
    }

    // The sign-in is newer than the one the request found and not older than the request that asked. Only the time the
    // sign-in wrote answers: a cookie without it falls back to its issue time, which the stamp check's renewal moves.
    public static bool IsAnswered(HttpContext context, OpenIddictRequest request, AuthenticationProperties cookie, DateTimeOffset now)
    {
        if ((string?)request.GetParameter(Parameter) is not { Length: > 0 } mark
            || !cookie.Items.TryGetValue(CmsSignInManager.SignedInAtItem, out var written)
            || !long.TryParse(written, NumberStyles.None, CultureInfo.InvariantCulture, out var signedIn))
        {
            return false;
        }

        string[] fields;
        try
        {
            fields = Protector(context).Unprotect(mark).Split('|');
        }
        catch (CryptographicException)
        {
            return false;
        }

        return fields.Length == 4
            && fields[0] == request.ClientId
            && fields[1] == request.CodeChallenge
            && long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var asked)
            && long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var found)
            && now - DateTimeOffset.FromUnixTimeSeconds(asked) <= Lifetime
            && signedIn >= asked
            && signedIn > found;
    }

    private static IDataProtector Protector(HttpContext context) =>
        context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);

    private static string Text(DateTimeOffset time) => time.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
}
