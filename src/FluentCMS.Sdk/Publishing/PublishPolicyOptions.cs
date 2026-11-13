namespace FluentCMS.Publishing;

/// <summary>
/// How entries of a type defined without a class are published, how many of their versions are kept and how long the
/// trash holds them, for <see cref="Schema.ITypeBuilder.PublishPolicy"/>. What is not set keeps the default of
/// <see cref="PublishPolicyAttribute"/>, read from the SDK when the options are made.
/// </summary>
public sealed record PublishPolicyOptions
{
    /// <summary>What publishing does about a reference to an entry that is not published or is in the trash; <see cref="UnpublishedReferencePolicy.Warn"/> by default.</summary>
    public UnpublishedReferencePolicy UnpublishedReferences { get; init; } = UnpublishedReferencePolicy.Warn;

    /// <summary>How many versions to keep per entry and locale, at least 1; <see cref="PublishPolicyAttribute.DefaultMaxVersions"/> by default. A version that was published is never pruned.</summary>
    public int MaxVersions { get; init; } = PublishPolicyAttribute.DefaultMaxVersions;

    /// <summary>How many days an entry stays in the trash before the purge job deletes it for good, from 1 to <see cref="PublishPolicyAttribute.MaxTrashRetentionDays"/>; <see cref="PublishPolicyAttribute.DefaultTrashRetentionDays"/> by default.</summary>
    public int TrashRetentionDays { get; init; } = PublishPolicyAttribute.DefaultTrashRetentionDays;
}
