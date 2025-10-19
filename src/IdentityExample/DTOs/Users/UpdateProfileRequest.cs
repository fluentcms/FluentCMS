using System.ComponentModel.DataAnnotations;

namespace IdentityExample.DTOs.Users;

public class UpdateProfileRequest
{
    [Required]
    [MinLength(3)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
