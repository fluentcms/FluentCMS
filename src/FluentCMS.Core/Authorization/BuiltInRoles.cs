namespace FluentCMS.Core.Authorization;

// The roles that the system migration seeds, with fixed ids so that every instance and every export agrees on them.
// These are the instance-wide ones; a site gets its own Public role when it is created.
public static class BuiltInRoles
{
    public const string SuperAdminName = "Super Admin";
    public const string EditorName = "Editor";
    public const string AuthorName = "Author";
    public const string PublicName = "Public";
    public const string MemberName = "Member";

    // The row rule that Author's grants name: an entry is the principal's own when it created it.
    public const string OwnEntriesRule = "own-entries";

    public static readonly Guid SuperAdmin = new("00000000-0000-7000-8000-000000000001");
    public static readonly Guid Editor = new("00000000-0000-7000-8000-000000000002");
    public static readonly Guid Author = new("00000000-0000-7000-8000-000000000003");
    public static readonly Guid Public = new("00000000-0000-7000-8000-000000000004");
    public static readonly Guid Member = new("00000000-0000-7000-8000-000000000005");
}
