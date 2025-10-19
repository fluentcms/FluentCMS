using AutoMapper;
using IdentityExample.Attributes;
using IdentityExample.DTOs.Roles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IdentityExample.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // All endpoints require authentication
public class RolesController(    RoleManager<Role> roleManager,    UserManager<User> userManager,    IMapper mapper,    ILogger<RolesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<RoleDto>>>> GetAllRoles()
    {
        try
        {
            var roles = await roleManager.Roles.ToListAsync();
            var roleDtos = mapper.Map<List<RoleDto>>(roles);

            return Ok(new ApiResponse<List<RoleDto>>
            {
                Success = true,
                Message = "Roles retrieved successfully",
                Data = roleDtos,
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while retrieving all roles");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while retrieving roles",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPost]
    [SuperAdminRequired] // Only SuperAdmin can create roles
    public async Task<ActionResult<ApiResponse<RoleDto>>> CreateRole(CreateRoleRequest request)
    {
        try
        {
            // Check if role already exists
            var existingRole = await roleManager.FindByNameAsync(request.Name);
            if (existingRole != null)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Role with this name already exists",
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var role = mapper.Map<Role>(request);
            var result = await roleManager.CreateAsync(role);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to create role",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var roleDto = mapper.Map<RoleDto>(role);

            logger.LogInformation("Role {RoleName} created successfully", role.Name);

            return CreatedAtAction(nameof(GetRole), new { id = role.Id }, new ApiResponse<RoleDto>
            {
                Success = true,
                Message = "Role created successfully",
                Data = roleDto,
                StatusCode = 201,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while creating role");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while creating role",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<RoleDto>>> GetRole(Guid id)
    {
        try
        {
            var role = await roleManager.FindByIdAsync(id.ToString());
            if (role == null)
            {
                return NotFound(new ApiResponse
                {
                    Success = false,
                    Message = "Role not found",
                    StatusCode = 404,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var roleDto = mapper.Map<RoleDto>(role);

            return Ok(new ApiResponse<RoleDto>
            {
                Success = true,
                Message = "Role retrieved successfully",
                Data = roleDto,
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while retrieving role {RoleId}", id);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while retrieving role",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPut("{id:guid}")]
    [SuperAdminRequired] // Only SuperAdmin can update roles
    public async Task<ActionResult<ApiResponse<RoleDto>>> UpdateRole(Guid id, UpdateRoleRequest request)
    {
        try
        {
            var role = await roleManager.FindByIdAsync(id.ToString());
            if (role == null)
            {
                return NotFound(new ApiResponse
                {
                    Success = false,
                    Message = "Role not found",
                    StatusCode = 404,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Check if another role with the same name exists
            if (role.Name != request.Name)
            {
                var existingRole = await roleManager.FindByNameAsync(request.Name);
                if (existingRole != null && existingRole.Id != role.Id)
                {
                    return BadRequest(new ApiResponse
                    {
                        Success = false,
                        Message = "Another role with this name already exists",
                        StatusCode = 400,
                        Timestamp = DateTime.UtcNow,
                        TraceId = HttpContext.TraceIdentifier
                    });
                }
            }

            mapper.Map(request, role);
            var result = await roleManager.UpdateAsync(role);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to update role",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var roleDto = mapper.Map<RoleDto>(role);

            logger.LogInformation("Role {RoleName} updated successfully", role.Name);

            return Ok(new ApiResponse<RoleDto>
            {
                Success = true,
                Message = "Role updated successfully",
                Data = roleDto,
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while updating role {RoleId}", id);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while updating role",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpDelete("{id:guid}")]
    [SuperAdminRequired] // Only SuperAdmin can delete roles
    public async Task<ActionResult<ApiResponse>> DeleteRole(Guid id)
    {
        try
        {
            var role = await roleManager.FindByIdAsync(id.ToString());
            if (role == null)
            {
                return NotFound(new ApiResponse
                {
                    Success = false,
                    Message = "Role not found",
                    StatusCode = 404,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Check if role is being used by any users
            var usersInRole = await userManager.GetUsersInRoleAsync(role.Name!);
            if (usersInRole.Any())
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = $"Cannot delete role. {usersInRole.Count} user(s) are assigned to this role.",
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var result = await roleManager.DeleteAsync(role);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to delete role",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            logger.LogInformation("Role {RoleName} deleted successfully", role.Name);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "Role deleted successfully",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while deleting role {RoleId}", id);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while deleting role",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpGet("/api/users/{userId:guid}/roles")]
    public async Task<ActionResult<ApiResponse<List<RoleDto>>>> GetUserRoles(Guid userId)
    {
        try
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
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

            var roleNames = await userManager.GetRolesAsync(user);
            var roles = new List<Role>();

            foreach (var roleName in roleNames)
            {
                var role = await roleManager.FindByNameAsync(roleName);
                if (role != null)
                {
                    roles.Add(role);
                }
            }

            var roleDtos = mapper.Map<List<RoleDto>>(roles);

            return Ok(new ApiResponse<List<RoleDto>>
            {
                Success = true,
                Message = "User roles retrieved successfully",
                Data = roleDtos,
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while retrieving roles for user {UserId}", userId);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while retrieving user roles",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPost("/api/users/{userId:guid}/roles")]
    [SuperAdminRequired] // Only SuperAdmin can assign roles
    public async Task<ActionResult<ApiResponse>> AssignRoleToUser(Guid userId, [Required] string roleName)
    {
        try
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
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

            var role = await roleManager.FindByNameAsync(roleName);
            if (role == null)
            {
                return NotFound(new ApiResponse
                {
                    Success = false,
                    Message = "Role not found",
                    StatusCode = 404,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Check if user already has this role
            if (await userManager.IsInRoleAsync(user, roleName))
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "User already has this role",
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var result = await userManager.AddToRoleAsync(user, roleName);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to assign role to user",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            logger.LogInformation("Role {RoleName} assigned to user {UserName}", roleName, user.UserName);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "Role assigned to user successfully",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while assigning role to user {UserId}", userId);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while assigning role to user",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpDelete("/api/users/{userId:guid}/roles/{roleId:guid}")]
    [SuperAdminRequired] // Only SuperAdmin can remove roles
    public async Task<ActionResult<ApiResponse>> RemoveRoleFromUser(Guid userId, Guid roleId)
    {
        try
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
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

            var role = await roleManager.FindByIdAsync(roleId.ToString());
            if (role == null)
            {
                return NotFound(new ApiResponse
                {
                    Success = false,
                    Message = "Role not found",
                    StatusCode = 404,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Check if user has this role
            if (!await userManager.IsInRoleAsync(user, role.Name!))
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "User does not have this role",
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var result = await userManager.RemoveFromRoleAsync(user, role.Name!);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = "Failed to remove role from user",
                    Errors = result.Errors.Select(e => new ApiError(e.Code, e.Description)).ToList(),
                    StatusCode = 400,
                    Timestamp = DateTime.UtcNow,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            logger.LogInformation("Role {RoleName} removed from user {UserName}", role.Name, user.UserName);

            return Ok(new ApiResponse
            {
                Success = true,
                Message = "Role removed from user successfully",
                StatusCode = 200,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while removing role from user {UserId}", userId);
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "An error occurred while removing role from user",
                StatusCode = 500,
                Timestamp = DateTime.UtcNow,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }
}
