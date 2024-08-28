namespace FluentCMS.Services.Setup.Models;

public class LayoutDetailModel : BaseAuditableModel
{
    public string Name { get; set; } = default!;
    public string Body { get; set; } = default!;
    public string Head { get; set; } = default!;
}
