﻿namespace FluentCMS.Web.Api.Models;

public class RoleCreateRequest
{
    [Required]
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    [Required]
    public ICollection<Policy> Policies { get; set; } = [];
}
