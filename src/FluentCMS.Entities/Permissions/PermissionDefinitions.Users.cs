namespace FluentCMS.Entities.Permissions;
public partial record PermissionDefinition
{
    [PermissionSet<User>] // We can find permission definitions with reflection, instead of querying db this is really performant for static Entities
    public static class Users
    {
        public const string Base = "Users"; // Make Private Or Keep Public? 
        public static PermissionDefinition Creation => new PermissionDefinition(string.Join(".", Base, nameof(Creation)));
        public static PermissionDefinition Modification => new PermissionDefinition(string.Join(".", Base, nameof(Modification)));
        public static PermissionDefinition Removal => new PermissionDefinition(string.Join(".", Base, nameof(Removal)));
        public static PermissionDefinition Query => new PermissionDefinition(string.Join(".", Base, nameof(Query)));
    }
}
