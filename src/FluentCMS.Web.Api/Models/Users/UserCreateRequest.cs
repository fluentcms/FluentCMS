﻿using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models;

public class UserCreateRequest
{
    [Required]
    public string Username { get; set; } = default!;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = default!;

    [Required]
    public string Password { get; set; } = default!;
}
