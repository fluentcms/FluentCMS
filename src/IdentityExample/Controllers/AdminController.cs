using AutoMapper;
using IdentityExample.Attributes;
using IdentityExample.DTOs.Admin;
using IdentityExample.Models;
using IdentityExample.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IdentityExample.Controllers;

[ApiController]
[Route("api/[controller]")]
[SuperAdminRequired] // All endpoints require SuperAdmin access
public class AdminController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly ICurrentUser _currentUser;
    private readonly ITokenService _tokenService;
    private readonly IMapper _mapper;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        UserManager<User> userManager,
        ICurrentUser currentUser,
        ITokenService tokenService,
        IMapper mapper,
        ILogger<AdminController> logger)
    {
        _userManager = userManager;
        _currentUser = currentUser;
        _tokenService = tokenService;
        _mapper = mapper;
        _logger = logger;
    }

    [HttpGet("users")]
    public async Task<ActionResult<ApiResponse<List<AdminUserDto>>>> GetAllUsers()
    {
        try
        {
            var users = await _userManager.Users.ToListAsync();
            var userDtos = _mapper.Map<List<AdminUserDto>>(users);

            return Ok(new ApiResponse<List<AdminUserDto>>
            {
                Success = true,
                Message = "Users retrieved successfully",
                Data = userDtos,
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while retrieving all users");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while retrieving users",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpGet("users/{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminUserDto>>> GetUser(Guid id)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new ApiResponse
                {
                    Success = false,
                    Message = "User not found",
                    StatusCode = 404,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var userDto = _mapper.Map<AdminUserDto>(user);

            return Ok(new ApiResponse<AdminUserDto>
            {
                Success = true,
                Message = "User retrieved successfully",
                Data = userDto,
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while retrieving user {UserId}", id);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while retrieving user",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPut("users/{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminUserDto>>> UpdateUser(Guid id, UpdateAdminUserRequest request)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new ApiResponse
                {
                    Success = false,
                    Message = "User not found",
                    StatusCode = 404,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Check if username is already taken by another user
            if (user.UserName != request.UserName)
            {
                var existingUser = await _userManager.FindByNameAsync(request.UserName);
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
                var existingUser = await _userManager.FindByEmailAsync(request.Email);
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
            _mapper.Map(request, user);

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to update user",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var updatedUser = _mapper.Map<AdminUserDto>(user);

            _logger.LogInformation("Admin {AdminName} updated user {UserName}", _currentUser.User?.UserName, user.UserName);

            return Ok(new ApiResponse<AdminUserDto>
            {
                Success = true,
                Message = "User updated successfully",
                Data = updatedUser,
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while updating user {UserId}", id);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while updating user",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPost("users/{id:guid}/suspend")]
    public async Task<ActionResult<ApiResponse>> SuspendUser(Guid id)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new ApiResponse
                {
                    Success = false,
                    Message = "User not found",
                    StatusCode = 404,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Cannot suspend self
            if (user.Id == _currentUser.UserId)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Cannot suspend yourself",
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            user.Suspended = true;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to suspend user",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Revoke all user tokens
            await _tokenService.RevokeAllUserTokensAsync(user.Id);

            _logger.LogInformation("Admin {AdminName} suspended user {UserName}", _currentUser.User?.UserName, user.UserName);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "User suspended successfully",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while suspending user {UserId}", id);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while suspending user",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPost("users/{id:guid}/unsuspend")]
    public async Task<ActionResult<ApiResponse>> UnsuspendUser(Guid id)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new ApiResponse
                {
                    Success = false,
                    Message = "User not found",
                    StatusCode = 404,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            user.Suspended = false;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to unsuspend user",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            _logger.LogInformation("Admin {AdminName} unsuspended user {UserName}", _currentUser.User?.UserName, user.UserName);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "User unsuspended successfully",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while unsuspending user {UserId}", id);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while unsuspending user",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPost("users/{id:guid}/unlock")]
    public async Task<ActionResult<ApiResponse>> UnlockUser(Guid id)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new ApiResponse
                {
                    Success = false,
                    Message = "User not found",
                    StatusCode = 404,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var result = await _userManager.SetLockoutEndDateAsync(user, null);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to unlock user",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Reset access failed count
            await _userManager.ResetAccessFailedCountAsync(user);

            _logger.LogInformation("Admin {AdminName} unlocked user {UserName}", _currentUser.User?.UserName, user.UserName);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "User unlocked successfully",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while unlocking user {UserId}", id);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while unlocking user",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpDelete("users/{id:guid}")]
    public async Task<ActionResult<ApiResponse>> DeleteUser(Guid id)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new ApiResponse
                {
                    Success = false,
                    Message = "User not found",
                    StatusCode = 404,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Cannot delete self
            if (user.Id == _currentUser.UserId)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Cannot delete yourself",
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Revoke all tokens first
            await _tokenService.RevokeAllUserTokensAsync(user.Id);

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to delete user",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            _logger.LogInformation("Admin {AdminName} deleted user {UserName}", _currentUser.User?.UserName, user.UserName);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "User deleted successfully",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while deleting user {UserId}", id);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while deleting user",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPut("users/{id:guid}/superadmin")]
    public async Task<ActionResult<ApiResponse>> GrantSuperAdmin(Guid id)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new ApiResponse
                {
                    Success = false,
                    Message = "User not found",
                    StatusCode = 404,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Cannot modify own SuperAdmin status
            if (user.Id == _currentUser.UserId)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Cannot modify your own SuperAdmin status",
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            if (user.IsSuperAdmin)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "User is already a SuperAdmin",
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            user.IsSuperAdmin = true;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to grant SuperAdmin status",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            _logger.LogInformation("Admin {AdminName} granted SuperAdmin status to user {UserName}", _currentUser.User?.UserName, user.UserName);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "SuperAdmin status granted successfully",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while granting SuperAdmin status to user {UserId}", id);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while granting SuperAdmin status",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpDelete("users/{id:guid}/superadmin")]
    public async Task<ActionResult<ApiResponse>> RevokeSuperAdmin(Guid id)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new ApiResponse
                {
                    Success = false,
                    Message = "User not found",
                    StatusCode = 404,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Cannot modify own SuperAdmin status
            if (user.Id == _currentUser.UserId)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Cannot modify your own SuperAdmin status",
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            if (!user.IsSuperAdmin)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "User is not a SuperAdmin",
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            user.IsSuperAdmin = false;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to revoke SuperAdmin status",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Revoke all tokens to force re-authentication with new permissions
            await _tokenService.RevokeAllUserTokensAsync(user.Id);

            _logger.LogInformation("Admin {AdminName} revoked SuperAdmin status from user {UserName}", _currentUser.User?.UserName, user.UserName);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "SuperAdmin status revoked successfully",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while revoking SuperAdmin status from user {UserId}", id);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while revoking SuperAdmin status",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpGet("tokens/active")]
    public async Task<ActionResult<ApiResponse<List<JwtToken>>>> GetActiveTokens()
    {
        try
        {
            var activeTokens = await _tokenService.GetActiveTokensAsync();

            return Ok(new ApiResponse<List<JwtToken>>
            {
                Success = true,
                Message = "Active tokens retrieved successfully",
                Data = activeTokens.ToList(),
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while retrieving active tokens");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while retrieving active tokens",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpGet("users/{id:guid}/tokens")]
    public async Task<ActionResult<ApiResponse<List<JwtToken>>>> GetUserTokens(Guid id)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new ApiResponse
                {
                    Success = false,
                    Message = "User not found",
                    StatusCode = 404,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var userTokens = await _tokenService.GetUserActiveTokensAsync(user.Id);

            return Ok(new ApiResponse<List<JwtToken>>
            {
                Success = true,
                Message = "User tokens retrieved successfully",
                Data = userTokens.ToList(),
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while retrieving user tokens for {UserId}", id);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while retrieving user tokens",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpDelete("tokens/{tokenId:guid}")]
    public async Task<ActionResult<ApiResponse>> InvalidateToken(Guid tokenId)
    {
        try
        {
            await _tokenService.RevokeTokenAsync(tokenId);

            _logger.LogInformation("Admin {AdminName} invalidated token {TokenId}", _currentUser.User?.UserName, tokenId);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "Token invalidated successfully",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while invalidating token {TokenId}", tokenId);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while invalidating token",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }
}
