using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Api.Account;

// An error that could not go back to the application that sent the user, such as an unknown client or redirect URI.
public sealed class ErrorModel : AccountPageModel
{
    private const int MaxDescription = 300;

    [BindProperty(SupportsGet = true, Name = Parameters.Error)]
    public string? Code { get; set; }

    [BindProperty(SupportsGet = true, Name = Parameters.ErrorDescription)]
    public string? Description { get; set; }

    public string Message => Code switch
    {
        Errors.InvalidClient or Errors.UnauthorizedClient => "The application that sent you here is not one this server knows.",
        Errors.InvalidRequest or Errors.InvalidScope or Errors.UnsupportedResponseType => "The application that sent you here asked for something this server does not allow.",
        Errors.AccessDenied => "You did not allow the application what it asked for.",
        _ => "Something went wrong. Go back to the application and try again.",
    };

    public string? Details => Description is { Length: > MaxDescription } text ? text[..MaxDescription] : Description;

    public void OnGet()
    {
        if (!string.IsNullOrEmpty(Code))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }
}
