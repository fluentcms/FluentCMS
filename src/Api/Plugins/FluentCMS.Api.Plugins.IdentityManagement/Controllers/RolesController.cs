using Microsoft.AspNetCore.Mvc;
using FluentCMS.Api.Plugins.IdentityManagement.Controllers.DTOs;

namespace FluentCMS.Api.Plugins.IdentityManagement.Controllers;

/// <summary>
/// Controller for Role Management operations
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Produces("application/json")]
public class RolesController(IRoleService roleService, IUserRoleService userRoleService, IUserService userService) : BaseController
{

    /// <summary>
    /// Get all roles with pagination, filtering, and ordering
    /// </summary>
    /// <param name="query">Query parameters for filtering and pagination</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated list of roles</returns>
    [HttpGet]
    public async Task<ActionResult<ApiListResponse<RoleDto>>> GetRoles([FromQuery] RoleQueryDto query, CancellationToken cancellationToken = default)
    {
        // Validate pagination parameters
        if (query.Page < 1) query.Page = 1;
        if (query.PageSize < 1) query.PageSize = 10;
        if (query.PageSize > 100) query.PageSize = 100;

        // Get all roles for the current site
        var siteId = SecurityContext.Site?.Id ?? throw new InvalidOperationException("Site context is required");
        var allRoles = await roleService.GetAllForSite(siteId, cancellationToken);

        // Convert to DTOs
        var roleDtos = allRoles.Select(MapToDto).ToList();

        // Apply filtering
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchTerm = query.Search.ToLowerInvariant();
            roleDtos = roleDtos.Where(r =>
                r.Name.ToLowerInvariant().Contains(searchTerm) ||
                (r.Description?.ToLowerInvariant().Contains(searchTerm) ?? false)
            ).ToList();
        }

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            roleDtos = roleDtos.Where(r =>
                r.Type.Equals(query.Type, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        // Apply sorting
        roleDtos = query.SortBy.ToLowerInvariant() switch
        {
            "name" => query.SortDirection.ToLowerInvariant() == "desc"
                ? roleDtos.OrderByDescending(r => r.Name).ToList()
                : roleDtos.OrderBy(r => r.Name).ToList(),
            "created" => query.SortDirection.ToLowerInvariant() == "desc"
                ? roleDtos.OrderByDescending(r => r.CreatedAt).ToList()
                : roleDtos.OrderBy(r => r.CreatedAt).ToList(),
            "updated" => query.SortDirection.ToLowerInvariant() == "desc"
                ? roleDtos.OrderByDescending(r => r.UpdatedAt).ToList()
                : roleDtos.OrderBy(r => r.UpdatedAt).ToList(),
            _ => roleDtos.OrderBy(r => r.Name).ToList()
        };

        // Apply pagination
        var totalCount = roleDtos.Count;
        var pagedRoles = roleDtos
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return SuccessList(pagedRoles, query.Page, query.PageSize, totalCount);
    }

    /// <summary>
    /// Maps Role entity to RoleDto
    /// </summary>
    private static RoleDto MapToDto(Role role)
    {
        return new RoleDto
        {
            Id = role.Id,
            Name = role.Name ?? string.Empty,
            Description = role.Description,
            SiteId = role.SiteId,
            Type = role.Type.ToString(),
            CreatedAt = role.CreatedAt,
            UpdatedAt = role.UpdatedAt
        };
    }

    /// <summary>
    /// Get a specific role by ID
    /// </summary>
    /// <param name="id">Role ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Role details</returns>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<RoleDto>>> GetRole(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var role = await roleService.GetById(id, cancellationToken);
        var roleDto = MapToDto(role);
        return Success(roleDto);
    }

    /// <summary>
    /// Create a new role
    /// </summary>
    /// <param name="createRoleDto">Role creation data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created role details</returns>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<RoleDto>>> CreateRole(
        [FromBody] CreateRoleDto createRoleDto,
        CancellationToken cancellationToken = default)
    {
        // Map DTO to entity
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = createRoleDto.Name,
            Description = createRoleDto.Description,
            SiteId = createRoleDto.SiteId,
            Type = RoleTypes.UserDefined,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Create the role
        var createdRole = await roleService.Add(role, cancellationToken);
        var roleDto = MapToDto(createdRole);

        return Success(roleDto);
    }

    /// <summary>
    /// Update an existing role
    /// </summary>
    /// <param name="id">Role ID</param>
    /// <param name="updateRoleDto">Role update data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated role details</returns>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<RoleDto>>> UpdateRole(
        Guid id,
        [FromBody] UpdateRoleDto updateRoleDto,
        CancellationToken cancellationToken = default)
    {
        // Get existing role to preserve properties not in DTO
        var existingRole = await roleService.GetById(id, cancellationToken);

        // Update the role properties
        existingRole.Name = updateRoleDto.Name;
        existingRole.Description = updateRoleDto.Description;
        existingRole.UpdatedAt = DateTime.UtcNow;

        // Update the role
        var updatedRole = await roleService.Update(existingRole, cancellationToken);
        var roleDto = MapToDto(updatedRole);

        return Success(roleDto);
    }

    /// <summary>
    /// Delete a role
    /// </summary>
    /// <param name="id">Role ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>No content response</returns>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> DeleteRole(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await roleService.Remove(id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Get users assigned to a specific role
    /// </summary>
    /// <param name="id">Role ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of users assigned to the role</returns>
    [HttpGet("{id:guid}/users")]
    public async Task<ActionResult<ApiListResponse<UserDto>>> GetRoleUsers(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Verify role exists
        var role = await roleService.GetById(id, cancellationToken);
        var siteId = SecurityContext.Site?.Id ?? throw new InvalidOperationException("Site context is required");

        // Get all users and filter by those who have this role
        var allUsers = await userService.GetAll(cancellationToken);
        var usersWithRole = new List<UserDto>();

        foreach (var user in allUsers)
        {
            var userRoles = await userRoleService.GetUserRoles(user.Id, siteId, cancellationToken);
            if (userRoles.Any(r => r.Id == id))
            {
                usersWithRole.Add(MapUserToDto(user));
            }
        }

        // For this endpoint, we'll return a simple list without complex pagination
        // since it's typically a smaller dataset
        var pagination = new PaginationInfo
        {
            Page = 1,
            PageSize = usersWithRole.Count,
            TotalPages = 1,
            TotalCount = usersWithRole.Count,
            HasNextPage = false,
            HasPreviousPage = false
        };

        return SuccessList(usersWithRole, pagination);
    }

    /// <summary>
    /// Maps User entity to UserDto
    /// </summary>
    private static UserDto MapUserToDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            FirstName = null, // User entity doesn't have FirstName property
            LastName = null   // User entity doesn't have LastName property
        };
    }

    // User-Role Management endpoints with different route patterns

    /// <summary>
    /// Get all roles assigned to a user
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of roles assigned to the user</returns>
    [HttpGet("/api/admin/users/{userId:guid}/roles")]
    public async Task<ActionResult<ApiListResponse<RoleDto>>> GetUserRoles(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var siteId = SecurityContext.Site?.Id ?? throw new InvalidOperationException("Site context is required");

        // Verify user exists
        var user = await userService.GetById(userId, cancellationToken);

        // Get user's roles
        var userRoles = await userRoleService.GetUserRoles(userId, siteId, cancellationToken);
        var roleDtos = userRoles.Select(MapToDto).ToList();

        // Simple pagination for user roles
        var pagination = new PaginationInfo
        {
            Page = 1,
            PageSize = roleDtos.Count,
            TotalPages = 1,
            TotalCount = roleDtos.Count,
            HasNextPage = false,
            HasPreviousPage = false
        };

        return SuccessList(roleDtos, pagination);
    }

    /// <summary>
    /// Assign roles to a user
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="assignRolesDto">Roles to assign</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>No content response</returns>
    [HttpPost("/api/admin/users/{userId:guid}/roles")]
    public async Task<ActionResult<ApiResponse>> AssignRolesToUser(
        Guid userId,
        [FromBody] AssignRolesDto assignRolesDto,
        CancellationToken cancellationToken = default)
    {
        // Verify user exists
        var user = await userService.GetById(userId, cancellationToken);

        // Assign each role to the user
        foreach (var roleId in assignRolesDto.RoleIds)
        {
            await userRoleService.AssignUserToRole(userId, roleId, cancellationToken);
        }

        return NoContent();
    }

    /// <summary>
    /// Remove a specific role from a user
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="roleId">Role ID to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>No content response</returns>
    [HttpDelete("/api/admin/users/{userId:guid}/roles/{roleId:guid}")]
    public async Task<ActionResult<ApiResponse>> RemoveRoleFromUser(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        // Verify user exists
        var user = await userService.GetById(userId, cancellationToken);

        // Remove the role from the user
        await userRoleService.RemoveUserFromRole(userId, roleId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Remove all roles from a user
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>No content response</returns>
    [HttpDelete("/api/admin/users/{userId:guid}/roles")]
    public async Task<ActionResult<ApiResponse>> RemoveAllRolesFromUser(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var siteId = SecurityContext.Site?.Id ?? throw new InvalidOperationException("Site context is required");

        // Verify user exists
        var user = await userService.GetById(userId, cancellationToken);

        // Get all current roles for the user
        var userRoles = await userRoleService.GetUserRoles(userId, siteId, cancellationToken);

        // Remove each role from the user
        foreach (var role in userRoles)
        {
            await userRoleService.RemoveUserFromRole(userId, role.Id, cancellationToken);
        }

        return NoContent();
    }

    /// <summary>
    /// Replace user's roles (bulk update)
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="replaceUserRolesDto">New roles to assign</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>No content response</returns>
    [HttpPut("/api/admin/users/{userId:guid}/roles")]
    public async Task<ActionResult<ApiResponse>> ReplaceUserRoles(
        Guid userId,
        [FromBody] ReplaceUserRolesDto replaceUserRolesDto,
        CancellationToken cancellationToken = default)
    {
        var siteId = SecurityContext.Site?.Id ?? throw new InvalidOperationException("Site context is required");

        // Verify user exists
        var user = await userService.GetById(userId, cancellationToken);

        // Get all current roles for the user
        var currentUserRoles = await userRoleService.GetUserRoles(userId, siteId, cancellationToken);

        // Remove all existing roles
        foreach (var role in currentUserRoles)
        {
            await userRoleService.RemoveUserFromRole(userId, role.Id, cancellationToken);
        }

        // Assign new roles
        foreach (var roleId in replaceUserRolesDto.RoleIds)
        {
            await userRoleService.AssignUserToRole(userId, roleId, cancellationToken);
        }

        return NoContent();
    }
}
