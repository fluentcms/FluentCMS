using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Dtos;

public class LayoutDto
{
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public string Name { get; set; }
    public string Head { get; set; }
    public string Body { get; set; }
}

public class LayoutAddRequest
{
    public required string Name { get; set; }
    public required Guid SiteId { get; set; }
    public string Head { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public class LayoutUpdateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Head { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}
