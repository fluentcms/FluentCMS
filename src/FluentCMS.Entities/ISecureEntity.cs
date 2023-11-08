namespace FluentCMS.Entities;

public interface ISecureEntity
{
    public List<Permission> Permissions { get; set; }
}