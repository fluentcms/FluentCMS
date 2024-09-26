namespace FluentCMS.Web.Api.Models;

public class PluginInitialCreateRequest
{
    [Required]
    public Guid DefinitionId { get; set; }

    [Required]
    public Guid PageId { get; set; }

    [Required]
    public string Section { get; set; } = default!;
}
