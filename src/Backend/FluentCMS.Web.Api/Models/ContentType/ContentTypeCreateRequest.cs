﻿namespace FluentCMS.Web.Api.Models;

public class ContentTypeCreateRequest
{
    [Required]
    public Guid SiteId { get; set; } = default!;

    [Required]
    [Slug]
    public string Slug { get; set; } = default!;

    [Required]
    public string Title { get; set; } = default!;
    public string? Description { get; set; } = default!;
}
