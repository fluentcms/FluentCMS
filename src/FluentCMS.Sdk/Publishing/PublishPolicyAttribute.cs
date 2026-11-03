namespace FluentCMS.Publishing;

/// <summary>Sets how entries of a content type are published, how many of their versions are kept and how long the trash holds them.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PublishPolicyAttribute : Attribute
{
    /// <summary>The number of versions kept per entry and locale when a type does not say.</summary>
    public const int DefaultMaxVersions = 50;

    /// <summary>The number of days an entry stays in the trash when a type does not say.</summary>
    public const int DefaultTrashRetentionDays = 30;

    /// <summary>What publishing does about a reference to an entry that is not published or is in the trash.</summary>
    public UnpublishedReferencePolicy UnpublishedReferences { get; set; }

    /// <summary>How many versions to keep per entry and locale. The oldest go first, and a version that was published is never pruned.</summary>
    public int MaxVersions { get; set; } = DefaultMaxVersions;

    /// <summary>How many days an entry stays in the trash before the purge job deletes it for good.</summary>
    public int TrashRetentionDays { get; set; } = DefaultTrashRetentionDays;
}
