﻿using FluentCMS.Web.Api.Validation;
using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models;

public class AppUpdateRequest
{
    [Required]
    public string Title { get; set; } = default!;
    public string? Description { get; set; }

    [Required]
    [Slug]
    public string Slug { get; set; } = default!;
}
