namespace FluentCMS.Api.Plugins.IdentityManagement.Dtos;

public class AccountLoginRequest
{
    [Required]
    public string UserName { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class LoginDto
{
    public string Token { get; set; } = default!;
}

public class AccountRegisterRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(3)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [MinLength(3)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Compare("Password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class AccountChangePasswordRequest
{
    [Required]
    public string UserName { get; set; } = string.Empty;
    [Required]
    public string OldPassword { get; set; } = string.Empty;
    [Required]
    [MinLength(3)]
    public string NewPassword { get; set; } = string.Empty;
    [Required]
    [Compare("NewPassword")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class AccountResetPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required]
    public string Token { get; set; } = string.Empty;
    [Required]
    [MinLength(3)]
    public string NewPassword { get; set; } = string.Empty;
    [Required]
    [Compare("NewPassword")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class AccountForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class AccountConfirmEmailRequest
{
    [Required]
    public string UserName { get; set; } = string.Empty;
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required]
    public string Token { get; set; } = string.Empty;
}

public class AccountResendConfirmationRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
