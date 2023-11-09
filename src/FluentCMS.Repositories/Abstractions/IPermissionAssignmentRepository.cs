using FluentCMS.Entities;
using FluentCMS.Entities.Permissions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Repositories.Abstractions;
public interface IPermissionAssignmentRepository : IGenericRepository<PermissionAssignment>
{
    Task<bool> CheckPermission(PermissionDefinition permission, Role role);
    Task<PermissionAssignment?> FindByRoleAndPermission(string value, Guid id);
}
