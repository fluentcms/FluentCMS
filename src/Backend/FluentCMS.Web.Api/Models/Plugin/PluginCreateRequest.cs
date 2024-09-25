namespace FluentCMS.Web.Api.Models;

public class PluginCreateRequest
{
    [Required]
    public Guid SiteId { get; set; }

    [Required]
    public Guid DefinitionId { get; set; }

    [Required]
    public Guid PageId { get; set; }

    public string? Section { get; set; }
}
