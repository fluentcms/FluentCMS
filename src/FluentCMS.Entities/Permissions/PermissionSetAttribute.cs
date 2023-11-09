
namespace FluentCMS.Entities.Permissions;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class PermissionSetAttribute<T> : Attribute
    where T : ISecureEntity
{
    
}