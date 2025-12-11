using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Dtos;

public class BlockDto
{
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public string Name { get; set; }
    public string Category { get; set; }
    public string? Description { get; set; }
    public string Content { get; set; }
}

public class BlockAddRequest
{
    public required string Name { get; set; }
    public required Guid SiteId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class BlockUpdateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
