namespace FluentCMS.Entities;

public class Role : AppAssociatedEntity
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
}
