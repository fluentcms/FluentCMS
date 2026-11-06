using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Api.Account;

// An error that could not go back to the application that sent the user, such as an unknown client or redirect URI. Anyone
// can link here, so the page shows its own words for the code and the code only when it looks like one, never text of
// the query; the server's description is in its log.
public sealed partial class ErrorModel : AccountPageModel
{
    [BindProperty(SupportsGet = true, Name = Parameters.Error)]
    public string? Code { get; set; }

    public string? ShownCode => Code is not null && ErrorCode().IsMatch(Code) ? Code : null;

    public string Message => Code switch
    {
        Errors.InvalidClient or Errors.UnauthorizedClient => "The application that sent you here is not one this server knows.",
        Errors.InvalidRequest or Errors.InvalidScope or Errors.UnsupportedResponseType => "The application that sent you here asked for something this server does not allow.",
        Errors.AccessDenied => "You did not allow the application what it asked for.",
        _ => "Something went wrong. Go back to the application and try again.",
    };

    public void OnGet()
    {
        if (!string.IsNullOrEmpty(Code))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }

    [GeneratedRegex("^[a-z_]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorCode();
}
