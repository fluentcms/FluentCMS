namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("Sites")]
public class SiteModel : AuditableEntityModel
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Urls { get; set; } = default!;
    public Guid LayoutId { get; set; }
    public Guid DetailLayoutId { get; set; }
    public Guid EditLayoutId { get; set; }
}
