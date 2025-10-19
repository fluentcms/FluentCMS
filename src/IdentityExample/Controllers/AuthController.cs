using IdentityExample.DTOs.Auth;
using IdentityExample.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace IdentityExample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(UserManager<User> userManager, SignInManager<User> signInManager, ITokenService tokenService, IEmailService emailService, ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<object>>> Register(RegisterRequest request)
    {
        try
        {
            // Check if user already exists
            var existingUser = await userManager.FindByEmailAsync(request.Email);
            if (existingUser != null)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "User with this email already exists",
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Create new user
            var user = new User
            {
                UserName = request.UserName,
                Email = request.Email,
                Description = request.Description,
                IsSuperAdmin = false // New users are not super admins by default
            };

            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to create user",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Generate email confirmation token
            var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);

            // Send confirmation email
            await emailService.SendEmailConfirmationAsync(user.Email!, user.UserName!, confirmationToken);

            logger.LogInformation("User {UserName} registered successfully", user.UserName);

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "User registered successfully. Please check your email for confirmation.",
                Data = new { UserId = user.Id, UserName = user.UserName, Email = user.Email },
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during user registration");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred during registration",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<object>>> Login(LoginRequest request)
    {
        try
        {
            var user = await userManager.FindByNameAsync(request.UserName);
            if (user == null)
            {
                return Unauthorized(new ApiResponse
                {
                    Success = false,
                    Message = "Invalid username or password",
                    StatusCode = 401,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Check if user is suspended
            if (user.Suspended)
            {
                return Unauthorized(new ApiResponse
                {
                    Success = false,
                    Message = "Account is suspended",
                    StatusCode = 401,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

            if (result.IsLockedOut)
            {
                return Unauthorized(new ApiResponse
                {
                    Success = false,
                    Message = "Account is locked due to multiple failed login attempts",
                    StatusCode = 401,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            if (!result.Succeeded)
            {
                return Unauthorized(new ApiResponse
                {
                    Success = false,
                    Message = "Invalid username or password",
                    StatusCode = 401,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Check if email is confirmed
            if (!user.EmailConfirmed)
            {
                return Unauthorized(new ApiResponse
                {
                    Success = false,
                    Message = "Email not confirmed. Please check your email for confirmation link.",
                    StatusCode = 401,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Generate JWT token
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var token = await tokenService.GenerateTokenAsync(user, ipAddress, request.DeviceInfo);

            // Update user login info
            user.LastLogin = DateTime.UtcNow;
            user.LoginCount++;
            await userManager.UpdateAsync(user);

            logger.LogInformation("User {UserName} logged in successfully", user.UserName);

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Login successful",
                Data = new
                {
                    Token = token,
                    User = new
                    {
                        user.Id,
                        user.UserName,
                        user.Email,
                        user.IsSuperAdmin,
                        user.LastLogin,
                        user.LoginCount
                    }
                },
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during login");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred during login",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPost("confirm-email")]
    public async Task<ActionResult<ApiResponse>> ConfirmEmail([Required] string userId, [Required] string token)
    {
        try
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Invalid user",
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var result = await userManager.ConfirmEmailAsync(user, token);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Email confirmation failed",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Send welcome email
            await emailService.SendWelcomeEmailAsync(user.Email!, user.UserName!);

            logger.LogInformation("Email confirmed for user {UserName}", user.UserName);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "Email confirmed successfully",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during email confirmation");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred during email confirmation",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPost("resend-confirmation")]
    public async Task<ActionResult<ApiResponse>> ResendConfirmation([Required] string email)
    {
        try
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                // Don't reveal if user exists or not for security
                return Ok(new ApiResponse
                {
                    Success = true,
                    Message = "If the email exists, a confirmation email has been sent",
                    StatusCode = 200,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            if (user.EmailConfirmed)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Email is already confirmed",
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
            await emailService.SendEmailConfirmationAsync(user.Email!, user.UserName!, confirmationToken);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "Confirmation email sent",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while resending confirmation email");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while sending confirmation email",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPost("forgot-password")]
    public async Task<ActionResult<ApiResponse>> ForgotPassword([Required] string email)
    {
        try
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                // Don't reveal if user exists or not for security
                return Ok(new ApiResponse
                {
                    Success = true,
                    Message = "If the email exists, a password reset email has been sent",
                    StatusCode = 200,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            await emailService.SendPasswordResetAsync(user.Email!, user.UserName!, resetToken);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "Password reset email sent",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during password reset request");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred during password reset request",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult<ApiResponse>> ResetPassword([Required] string email, [Required] string token, [Required] string newPassword)
    {
        try
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Invalid reset request",
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var result = await userManager.ResetPasswordAsync(user, token, newPassword);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Password reset failed",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Update password change tracking
            user.PasswordChangedAt = DateTime.UtcNow;
            user.PasswordChangedBy = "Self";
            await userManager.UpdateAsync(user);

            logger.LogInformation("Password reset successfully for user {UserName}", user.UserName);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "Password reset successfully",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during password reset");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred during password reset",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }
}
