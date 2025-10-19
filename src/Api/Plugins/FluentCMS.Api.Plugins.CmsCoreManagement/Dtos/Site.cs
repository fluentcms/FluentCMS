using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Dtos;

public class SiteDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public List<string> Urls { get; set; } = [];
}

public class SiteAddRequest
{
    [Required]
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    [Required]
    public List<string> Urls { get; set; } = [];
}

public class SiteUpdateRequest
{
    [Required]
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    [Required]
    public List<string> Urls { get; set; } = [];
}
