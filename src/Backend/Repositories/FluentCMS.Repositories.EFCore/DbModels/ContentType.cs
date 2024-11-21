﻿namespace FluentCMS.Repositories.EFCore.DbModels;

public class ContentType : SiteAssociatedEntity
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public ICollection<ContentTypeField> Fields { get; set; } = [];
}
