using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Web.Api.Models;

public class PermissionResponse
{
    public Guid RoleId { get; set; } = default!;
    public List<Policy> Policies { get; set; } = [];

}
