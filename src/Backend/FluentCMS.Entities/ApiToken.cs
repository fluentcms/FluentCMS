namespace FluentCMS.Entities;

public class ApiToken : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Token { get; set; } = default!;
    public DateTime? ExpiredAt { get; set; }
    public bool Enabled { get; set; } = true;
    public ICollection<Policy> Policies { get; set; } = [];
}
