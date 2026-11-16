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

    private const char Separator = '|';

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    // The sign-in found is the cookie's, or none for a cookie that does not say when it signed in. Each field is escaped, so
    // that a client id with the separator in it keeps its place.
    public static IResult Challenge(HttpContext context, OpenIddictRequest request, List<KeyValuePair<string, StringValues>> parameters, DateTimeOffset? found, DateTimeOffset now, string scheme)
    {
        string[] fields = [request.ClientId ?? "", request.CodeChallenge ?? "", Text(now), Text(found ?? DateTimeOffset.UnixEpoch)];
        var mark = Protector(context).Protect(string.Join(Separator, fields.Select(Uri.EscapeDataString)));
        var marked = parameters.Where(parameter => parameter.Key != Parameter).Append(KeyValuePair.Create(Parameter, new StringValues(mark)));
        return Results.Challenge(new AuthenticationProperties { RedirectUri = context.Request.PathBase + context.Request.Path + QueryString.Create(marked) }, [scheme]);
    }

    // The sign-in, whose time the cookie wrote, is newer than the one the request found and not older than the request that
    // asked.
    public static bool IsAnswered(HttpContext context, OpenIddictRequest request, DateTimeOffset signedInAt, DateTimeOffset now)
    {
        if ((string?)request.GetParameter(Parameter) is not { Length: > 0 } mark)
        {
            return false;
        }

        string[] fields;
        try
        {
            fields = [.. Protector(context).Unprotect(mark).Split(Separator).Select(Uri.UnescapeDataString)];
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
            && signedInAt.ToUnixTimeSeconds() >= asked
            && signedInAt.ToUnixTimeSeconds() > found;
    }

    private static IDataProtector Protector(HttpContext context) =>
        context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);

    private static string Text(DateTimeOffset time) => time.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
}
