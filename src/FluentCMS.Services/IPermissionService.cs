using FluentCMS.Entities;
using FluentCMS.Entities.Permissions;
using FluentCMS.Repositories.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Services;
public interface IPermissionService
{
    Task<IEnumerable<Entities.Permissions.PermissionDefinition>> GetPermissionDefinitionsAsync<T>() where T : ISecureEntity;
    Task<IEnumerable<Entities.Permissions.PermissionDefinition>> GetPermissionDefinitionsAsync(string ContentTypeName);
    Task<bool> CheckPermission(Entities.Permissions.PermissionDefinition permission, Role role);
    Task AddPermission(Entities.Permissions.PermissionDefinition permission, Role role);
    Task RemovePermission(Entities.Permissions.PermissionDefinition permission, Role role);
}

public class PermissionService : IPermissionService
{
    private readonly IPermissionAssignmentRepository _permissionAssignmentRepository;
    public PermissionService(IPermissionAssignmentRepository permissionAssignmentRepository)
    {
        _permissionAssignmentRepository = permissionAssignmentRepository;
    }

    public async Task AddPermission(PermissionDefinition permission, Role role)
    {
        var permissionAssignment = new PermissionAssignment() { PermissionDefinition = permission, RoleId = role.Id };
        await _permissionAssignmentRepository.Create(permissionAssignment);
    }

    public async Task<bool> CheckPermission(PermissionDefinition permission, Role role)
    {
        return await _permissionAssignmentRepository.CheckPermission(permission, role);
    }

    public Task<IEnumerable<PermissionDefinition>> GetPermissionDefinitionsAsync<T>() where T : ISecureEntity
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<PermissionDefinition>> GetPermissionDefinitionsAsync(string ContentTypeName)
    {
        throw new NotImplementedException();
    }

    public async Task RemovePermission(PermissionDefinition permission, Role role)
    {
        var assignment = await _permissionAssignmentRepository.FindByRoleAndPermission(permission.Value, role.Id);
        await _permissionAssignmentRepository.Delete(assignment?.Id ?? throw new ApplicationException("Not Found!"));
    }
}
