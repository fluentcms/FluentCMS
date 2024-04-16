using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models.Users;

public class UserUpdateProfileRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = default!;
    public string PhoneNumber { get; set; } = default!;
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
}
