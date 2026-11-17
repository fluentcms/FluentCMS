namespace FluentCMS.Admin.Api;

// The answer of a publish (spec 4.10): the entry as published, and what the server warns about its references. Only the
// form reads it, so it stays internal.
internal sealed record PublishAnswer
{
    public required ContentEntry Entry { get; init; }

    public IReadOnlyList<PublishWarningInfo> Warnings { get; init; } = [];
}

// A reference of the published entry to one that is not published or cannot be reached; the target is named only when the
// caller may read it.
internal sealed record PublishWarningInfo
{
    public string Path { get; init; } = "";

    public string Code { get; init; } = "";

    public string Message { get; init; } = "";

    public string? TargetType { get; init; }

    public Guid? TargetId { get; init; }
}

// A publish's answer as the form goes on with it: the read in the locale it published, and the warnings.
internal sealed record PublishedRead(EntryRead Read, IReadOnlyList<PublishWarningInfo> Warnings);
