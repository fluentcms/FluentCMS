using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

/// <summary>
/// RoleController manages role-related operations.
/// </summary>
public class RoleController(IMapper mapper, IRoleService roleService) : BaseController
{
    /// <summary>
    /// Retrieves all roles associated with a specific site.
    /// </summary>
    /// <param name="siteId">The unique identifier of the site.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>A paginated list of roles for the specified site.</returns>
    [HttpGet("{siteId}")]
    public async Task<IApiPagingResult<Role>> GetAll([FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        var roles = await roleService.GetAll(siteId, cancellationToken);
        return new ApiPagingResult<Role>(roles);
    }

    /// <summary>
    /// Retrieves a specific role by its identifier for a given site.
    /// </summary>
    /// <param name="id">The unique identifier of the role.</param>
    /// <param name="siteId">The unique identifier of the site.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>The role details if found, or null.</returns>
    [HttpGet("{siteId}/{id}")]
    public async Task<IApiResult<Role>> GetById([FromRoute] Guid id, [FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        var role = await roleService.GetById(id, siteId, cancellationToken);
        return new ApiResult<Role>(role);
    }

    /// <summary>
    /// Creates a new role in the system.
    /// </summary>
    /// <param name="request">The role creation request containing the role details.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>The details of the newly created role.</returns>
    [HttpPost]
    public async Task<IApiResult<Role>> Create([FromBody] RoleCreateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var newRole = await roleService.Create(role, cancellationToken);
        return new ApiResult<Role>(newRole);
    }

    /// <summary>
    /// Updates an existing role.
    /// </summary>
    /// <param name="request">The role update request containing the updated role details.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>The details of the updated role.</returns>
    [HttpPut]
    public async Task<IApiResult<Role>> Update([FromBody] RoleUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var editedRole = await roleService.Update(role, cancellationToken);
        return new ApiResult<Role>(editedRole);
    }

    /// <summary>
    /// Deletes a specific role from a site.
    /// </summary>
    /// <param name="id">The unique identifier of the role to be deleted.</param>
    /// <param name="siteId">The unique identifier of the site from which the role is to be deleted.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>A boolean value indicating whether the deletion was successful.</returns>
    [HttpDelete("{siteId}/{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, [FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        await roleService.Delete(id, siteId, cancellationToken);
        return new ApiResult<bool>(true);
    }
}
