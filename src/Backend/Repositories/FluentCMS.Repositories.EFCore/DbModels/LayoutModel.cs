namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("Layouts")]
public class LayoutModel : SiteAssociatedEntityModel
{
    public string Name { get; set; } = default!;
    public string Body { get; set; } = default!;
    public string Head { get; set; } = default!;
}
