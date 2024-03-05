using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models.Users;

public class UserUpdateProfileRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; }
}
