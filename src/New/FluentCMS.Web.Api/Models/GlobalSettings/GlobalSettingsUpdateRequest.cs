﻿namespace FluentCMS.Web.Api.Models;

public class GlobalSettingsUpdateRequest
{
    public List<string> SuperUsers { get; set; } = [];
}
