﻿using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models;

public class UserUpdateRequest
{
    [Required]
    public required Guid Id { get; set; }
    [Required]
    [EmailAddress]
    public required string Email { get; set; }
}
