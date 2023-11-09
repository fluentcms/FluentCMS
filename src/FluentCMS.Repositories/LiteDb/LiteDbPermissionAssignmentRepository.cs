using FluentCMS.Entities;
using FluentCMS.Entities.Permissions;
using FluentCMS.Repositories.Abstractions;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Repositories.LiteDb;
public class LiteDbPermissionAssignmentRepository : LiteDbGenericRepository<PermissionAssignment>, IPermissionAssignmentRepository
{
    public LiteDbPermissionAssignmentRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<bool> CheckPermission(PermissionDefinition permission, Role role)
    {
        return await Collection.FindOneAsync(x => x.PermissionDefinition.Value == permission.Value && x.RoleId == role.Id) != null;
    }

    public async Task<PermissionAssignment?> FindByRoleAndPermission(string permission, Guid roleId)
    {
        return await Collection.FindOneAsync(x => x.PermissionDefinition.Value == permission && x.RoleId == roleId);
    }
}
