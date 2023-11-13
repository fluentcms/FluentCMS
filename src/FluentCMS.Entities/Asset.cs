namespace FluentCMS.Entities;

public class Asset : AuditEntity
{
    public string PhysicalFileName { get; set; } = string.Empty;
    public string VirtualFileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public Guid SiteId { get; set; }
}
