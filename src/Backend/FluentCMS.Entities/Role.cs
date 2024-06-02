namespace FluentCMS.Entities;

public class Role : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public ICollection<Policy> Policies { get; set; } = [];
    public bool ReadOnly { get; set; } // indicates if users can delete/update this role or not 
    public bool IsAdmin { get; set; } // indicates if this role has full access to the system
}
