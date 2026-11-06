using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Core.Api.Account;

// Asks before signing out. The form posts the request of the client, if one sent the user here, to /connect/logout, which
// ends the sign-in and returns to the client or to this page. There is no sign-out by GET.
public sealed class LogoutModel : AccountPageModel
{
    public string? UserName { get; private set; }

    public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; private set; } = [];

    // Nothing to ask: no one is signed in, and no client waits to be sent back to.
    public bool SignedOut => UserName is null && Parameters.Count == 0;

    public async Task OnGetAsync()
    {
        var cookie = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        UserName = cookie.Succeeded ? cookie.Principal.Identity?.Name : null;
        Parameters = [.. Request.Query.SelectMany(parameter => parameter.Value.Select(value => KeyValuePair.Create(parameter.Key, value ?? "")))];
    }
}
