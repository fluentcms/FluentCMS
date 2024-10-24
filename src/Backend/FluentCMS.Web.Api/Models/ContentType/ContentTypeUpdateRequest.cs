﻿namespace FluentCMS.Web.Api.Models;

public class ContentTypeUpdateRequest
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public string Title { get; set; } = default!;

    public string? Description { get; set; } = default!;
}
