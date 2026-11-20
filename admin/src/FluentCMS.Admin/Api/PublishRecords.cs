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

// The answer of POST .../schedule: when the action runs, as an instant.
internal sealed record ScheduleAnswer
{
    public string Action { get; init; } = "";

    public string? Locale { get; init; }

    public DateTimeOffset RunAt { get; init; }

    public string? TimeZone { get; init; }
}

// The answer of POST .../preview-token (#3099): the front end's page of the draft with the token in it, the published page,
// null while the locale was never published, and when the token ends. The token itself is read only to keep it out of an
// address the pane shows.
internal sealed record PreviewTokenAnswer
{
    public string? Token { get; init; }

    public string? Url { get; init; }

    public string? PublishedUrl { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
}
