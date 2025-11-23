using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Dtos;

public class LayoutDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Head { get; set; }
    public string Body { get; set; }
}

public class LayoutAddRequest
{
    public string Name { get; set; }
    public string Head { get; set; }
    public string Body { get; set; }
}

public class LayoutUpdateRequest
{
    public string Name { get; set; }
    public string Head { get; set; }
    public string Body { get; set; }
}
