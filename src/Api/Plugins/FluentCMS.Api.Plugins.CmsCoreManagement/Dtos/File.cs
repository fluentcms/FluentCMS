using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Dtos;

public class FileDto
{
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public string Name { get; set; }
    public string NormalizedName { get; set; } = default!;
    public Guid FolderId { get; set; }
    public string Extension { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long Size { get; set; }
}

public class FileMoveRequest
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
}

public class FileRenameRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}
