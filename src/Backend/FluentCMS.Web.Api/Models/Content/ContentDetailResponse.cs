﻿namespace FluentCMS.Web.Api.Models;

public class ContentDetailResponse : BaseAuditableResponse
{
    [Required]
    public Guid TypeId { get; set; }

    [Required]
    public Dictionary<string, object?> Data { get; set; } = [];

}
