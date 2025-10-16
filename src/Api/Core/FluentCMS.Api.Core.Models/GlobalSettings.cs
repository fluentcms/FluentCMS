namespace FluentCMS.Api.Core.Models;

public class GlobalSettings : AuditableEntity
{
    public List<string> SuperAdmins { get; set; } = [];
}

