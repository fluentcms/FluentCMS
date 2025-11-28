using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Dtos;

public class FolderDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string NormalizedName { get; set; }
    public List<FileDto> Files { get; set; } = [];
    public List<FolderDto> Folders { get; set; } = [];
    public FolderDto? ParentFolder { get; set; }
}

public class FolderAddRequest
{
    public string Name { get; set; }
    public Guid ParentId { get; set; }
}

public class FolderRenameRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}

public class FolderMoveRequest
{
    public Guid Id { get; set; }
    public Guid ParentId { get; set; }
}
