namespace FluentCMS.Admin.Api;

// Only the form reads these, so they stay internal.
internal sealed record PublishAnswer
{
    public required ContentEntry Entry { get; init; }

    public IReadOnlyList<PublishWarningInfo> Warnings { get; init; } = [];
}

// The target is named only when the caller may read it.
internal sealed record PublishWarningInfo
{
    public string Path { get; init; } = "";

    public string Code { get; init; } = "";

    public string Message { get; init; } = "";

    public string? TargetType { get; init; }

    public Guid? TargetId { get; init; }
}

internal sealed record PublishedRead(EntryRead Read, IReadOnlyList<PublishWarningInfo> Warnings);
