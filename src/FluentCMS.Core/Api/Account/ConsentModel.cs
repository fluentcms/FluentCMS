using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Oidc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Api.Account;

// The authorize endpoint sends the user here, with its request in the query, before a third-party client gets a code.
// Allowing records the consent and goes back to the endpoint, which then issues the code; refusing goes back with a
// parameter that makes the endpoint answer access_denied. The endpoint checks the request again either way.
public sealed class ConsentModel : AccountPageModel
{
    public string ClientName { get; private set; } = "";

    public IReadOnlyList<string> Scopes { get; private set; } = [];

    public string? UserName { get; private set; }

    public static string Describe(string scope) => scope switch
    {
        OpenIddict.Abstractions.OpenIddictConstants.Scopes.OpenId => "Know who you are",
        OpenIddict.Abstractions.OpenIddictConstants.Scopes.Profile => "See your user name",
        OpenIddict.Abstractions.OpenIddictConstants.Scopes.Email => "See your email address",
        OpenIddict.Abstractions.OpenIddictConstants.Scopes.OfflineAccess => "Stay connected while you are away",
        TokenScopes.Management => "Manage content and settings, as far as your roles allow",
        TokenScopes.Draft => "Read drafts, as far as your roles allow",
        _ => scope,
    };

    public async Task<IActionResult> OnGetAsync()
    {
        if (await SignedInUserAsync() is not { } user)
        {
            return RedirectToLogin(AuthorizeUrl(Request.Query));
        }

        if (await DescribeAsync() is not { } request)
        {
            return NotAClient();
        }

        UserName = user.UserName;
        ClientName = request.ClientName;
        Scopes = request.Scopes;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? decision)
    {
        if (decision != "allow")
        {
            var refused = Request.Query.ToDictionary(parameter => parameter.Key, parameter => parameter.Value, StringComparer.Ordinal);
            refused[ClientConsents.DecisionParameter] = ClientConsents.Denied;
            return Redirect(AuthorizeUrl(refused));
        }

        if (await SignedInUserAsync() is not { } user)
        {
            return RedirectToLogin(AuthorizeUrl(Request.Query));
        }

        var clientId = Request.Query[Parameters.ClientId].ToString();
        if (!await Service<ClientConsents>().GrantAsync(user.Id, clientId, RequestedScopes(), HttpContext.RequestAborted))
        {
            return NotAClient();
        }

        // Consent was just given, so the request goes back without the prompt for it.
        var parameters = Request.Query.ToDictionary(parameter => parameter.Key, parameter => parameter.Value, StringComparer.Ordinal);
        if (parameters.TryGetValue(Parameters.Prompt, out var prompt))
        {
            var rest = prompt.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(value => value != PromptValues.Consent).ToArray();
            if (rest.Length == 0)
            {
                parameters.Remove(Parameters.Prompt);
            }
            else
            {
                parameters[Parameters.Prompt] = string.Join(' ', rest);
            }
        }

        return Redirect(AuthorizeUrl(parameters));
    }

    private Task<ConsentRequest?> DescribeAsync() =>
        Service<ClientConsents>().DescribeAsync(Request.Query[Parameters.ClientId].ToString(), RequestedScopes(), HttpContext.RequestAborted);

    private string[] RequestedScopes() =>
        Request.Query[Parameters.Scope].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private RedirectResult NotAClient() =>
        Redirect(Request.PathBase + HostedPagePaths.Error + QueryString.Create(Parameters.Error, Errors.InvalidRequest));

    private string AuthorizeUrl(IEnumerable<KeyValuePair<string, StringValues>> parameters) =>
        Request.PathBase + "/connect/authorize" + QueryString.Create(parameters);
}
