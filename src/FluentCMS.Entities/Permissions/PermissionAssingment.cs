using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Entities.Permissions;
public class PermissionAssignment:IEntity
{
    public required PermissionDefinition PermissionDefinition { get; set; }
    public Guid RoleId { get; set; }
    public Guid Id { get; set; }
}
