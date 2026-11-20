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
// null while the locale was never published, when the token ends by the server's clock, the token's lifetime in seconds as
// issued (#3473, while the server sends it), and the server's clock when it answered (its Date header, when the browser
// exposes it). The token itself is read only to keep it out of an address the pane shows.
internal sealed record PreviewTokenAnswer
{
    public string? Token { get; init; }

    public string? Url { get; init; }

    public string? PublishedUrl { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    // A number, so that one with a fraction still reads; the pane takes its whole seconds.
    public double? ExpiresIn { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset? AnsweredAt { get; init; }
}
