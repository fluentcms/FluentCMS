﻿namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("Files")]
public class FileModel : SiteAssociatedEntityModel
{
    public string Name { get; set; } = default!;
    public string NormalizedName { get; set; } = default!;
    public Guid FolderId { get; set; } // Foreign key
    public string Extension { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long Size { get; set; }

    public FolderModel Folder { get; set; } = default!; // Navigation property
}
