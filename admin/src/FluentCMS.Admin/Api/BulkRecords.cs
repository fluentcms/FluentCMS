namespace FluentCMS.Admin.Api;

// The operations of POST /content/{type}/bulk the list offers, by the name the server takes.
internal enum BulkOperation
{
    Publish,
    Unpublish,
    Delete,
}

// The answer of a bulk call: one item per entry, in the order sent, and whether the server stopped before the end.
internal sealed record BulkAnswer
{
    public IReadOnlyList<BulkAnswerItem> Items { get => field ?? []; init; }

    public bool Cancelled { get; init; }
}

// The status is succeeded, failed, duplicate or notRun; a failed entry has its problem, and a publish its warnings.
internal sealed record BulkAnswerItem
{
    public int Index { get; init; }

    public Guid? Id { get; init; }

    public string Status { get; init; } = "";

    public int? Version { get; init; }

    public IReadOnlyList<PublishWarningInfo> Warnings { get => field ?? []; init; }

    public ApiProblem? Problem { get; init; }
}
