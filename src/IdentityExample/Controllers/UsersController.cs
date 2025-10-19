using AutoMapper;
using IdentityExample.DTOs.Users;
using IdentityExample.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace IdentityExample.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // All endpoints require authentication
public class UsersController(
    UserManager<User> userManager,
    ICurrentUser currentUser,
    ITokenService tokenService,
    IMapper mapper,
    ILogger<UsersController> logger) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<ActionResult<ApiResponse<UserProfileDto>>> GetProfile()
    {
        try
        {
            var user = currentUser.User;
            if (user == null)
            {
                return Unauthorized(new ApiResponse
                {
                    Success = false,
                    Message = "User not found",
                    StatusCode = 401,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var userProfile = mapper.Map<UserProfileDto>(user);

            return Ok(new ApiResponse<UserProfileDto>
            {
                Success = true,
                Message = "Profile retrieved successfully",
                Data = userProfile,
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while retrieving user profile");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while retrieving profile",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPut("profile")]
    public async Task<ActionResult<ApiResponse<UserProfileDto>>> UpdateProfile(UpdateProfileRequest request)
    {
        try
        {
            var user = currentUser.User;
            if (user == null)
            {
                return Unauthorized(new ApiResponse
                {
                    Success = false,
                    Message = "User not found",
                    StatusCode = 401,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Check if username is already taken by another user
            if (user.UserName != request.UserName)
            {
                var existingUser = await userManager.FindByNameAsync(request.UserName);
                if (existingUser != null && existingUser.Id != user.Id)
                {
                    return BadRequest(new ApiResponse
                    {
                        Success = false,
                        Message = "Username is already taken",
                        StatusCode = 400,
                        Timestamp = DateTime.UtcNow,
                        TraceId = HttpContext.TraceIdentifier
                    });
                }
            }

            // Check if email is already taken by another user
            if (user.Email != request.Email)
            {
                var existingUser = await userManager.FindByEmailAsync(request.Email);
                if (existingUser != null && existingUser.Id != user.Id)
                {
                    return BadRequest(new ApiResponse
                    {
                        Success = false,
                        Message = "Email is already taken",
                        StatusCode = 400,
                        Timestamp = DateTime.UtcNow,
                        TraceId = HttpContext.TraceIdentifier
                    });
                }
            }

            // Update user properties
            mapper.Map(request, user);

            var result = await userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to update profile",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var updatedProfile = mapper.Map<UserProfileDto>(user);

            logger.LogInformation("User {UserName} updated profile successfully", user.UserName);

            return Ok(new ApiResponse<UserProfileDto>
            {
                Success = true,
                Message = "Profile updated successfully",
                Data = updatedProfile,
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while updating user profile");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while updating profile",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPost("change-password")]
    public async Task<ActionResult<ApiResponse>> ChangePassword(ChangePasswordRequest request)
    {
        try
        {
            var user = currentUser.User;
            if (user == null)
            {
                return Unauthorized(new ApiResponse
                {
                    Success = false,
                    Message = "User not found",
                    StatusCode = 401,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to change password",
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

            // Revoke all existing tokens to force re-login
            await tokenService.RevokeAllUserTokensAsync(user.Id);

            logger.LogInformation("User {UserName} changed password successfully", user.UserName);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "Password changed successfully. Please login again.",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while changing password");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while changing password",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpDelete("account")]
    public async Task<ActionResult<ApiResponse>> DeleteAccount()
    {
        try
        {
            var user = currentUser.User;
            if (user == null)
            {
                return Unauthorized(new ApiResponse
                {
                    Success = false,
                    Message = "User not found",
                    StatusCode = 401,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Prevent SuperAdmin from deleting their own account
            if (user.IsSuperAdmin)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "SuperAdmin cannot delete their own account",
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Revoke all tokens first
            await tokenService.RevokeAllUserTokensAsync(user.Id);

            var result = await userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to delete account",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            logger.LogInformation("User {UserName} deleted their account", user.UserName);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "Account deleted successfully",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while deleting account");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while deleting account",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }
}
