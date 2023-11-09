using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Entities.Permissions;
public partial record PermissionDefinition(string Value){}

public partial record DynamicPermissionDefinition(string ContentTypeName, string Value): PermissionDefinition(Value) { }

public partial record DynamicPermissionSetDefinition(string ContentTypeName)
{
    public DynamicPermissionDefinition Creation => new(ContentTypeName,nameof(Creation));
    public DynamicPermissionDefinition Modification => new(ContentTypeName,nameof(Modification));
    public DynamicPermissionDefinition Removal => new(ContentTypeName,nameof(Removal));
    public DynamicPermissionDefinition Query => new(ContentTypeName, nameof(Query));
    public DynamicPermissionDefinition GetById => new(ContentTypeName, nameof(GetById));
}
