﻿namespace FluentCMS.Web.Api.Models;

public class UserDetailResponse : BaseAuditableResponse
{
    public string Username { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? PhoneNumber { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int LoginCount { get; set; }
    public bool Enabled { get; set; }
}
