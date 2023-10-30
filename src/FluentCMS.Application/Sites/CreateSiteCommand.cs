using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Application.Sites;
public class CreateSiteCommand : IRequest<Guid>
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] URLs { get; set; } = [];
    public Guid RoleId { get; set; }
}
