﻿namespace FluentCMS.Web.Api.Models;

public class FolderCreateRequest
{
    [Required]
    public string Name { get; set; } = default!;

    public Guid? ParentId { get; set; }
}
