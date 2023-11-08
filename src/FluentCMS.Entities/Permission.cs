namespace FluentCMS.Entities;

public class Permission
{
    public string Name { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
}