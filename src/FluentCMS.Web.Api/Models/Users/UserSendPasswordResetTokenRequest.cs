using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models.Users;

public class UserSendPasswordResetTokenRequest
{

    [Required] public string Email { get; set; } = string.Empty;
}
