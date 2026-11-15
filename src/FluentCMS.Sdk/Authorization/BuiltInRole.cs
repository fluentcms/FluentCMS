namespace FluentCMS.Authorization;

/// <summary>A built-in role that a permission can be given to by default; see <see cref="Permission.DefaultRoles"/>.</summary>
public enum BuiltInRole
{
    /// <summary>Editor, which creates, edits, publishes and removes any content.</summary>
    Editor = 1,

    /// <summary>Author, which creates content and reads, edits or removes only its own entries.</summary>
    Author = 2,
}
