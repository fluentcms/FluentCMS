using System.ComponentModel.DataAnnotations;

namespace IdentityExample.DTOs.Auth;

public class LoginRequest
{
    [Required]
    public string UserName { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public string DeviceInfo { get; set; } = string.Empty;
}
