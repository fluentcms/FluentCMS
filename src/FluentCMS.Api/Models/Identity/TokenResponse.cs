namespace FluentCMS.Api.Models.Identity;

public record TokenResponse(string AccessToken, string RefreshToken)
{
}