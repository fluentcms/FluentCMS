namespace FluentCMS.Activity;

/// <summary>
/// What happened and to what, as an activity row records it. Who did it, from where and when come from the operation or
/// the event, never from the entry. Nothing in an entry may be a field value of content: the log is read by people who
/// may not be allowed to read the content.
/// </summary>
/// <param name="Action">What happened, in the form <c>area.verb</c>, such as <c>content.published</c>. At most 64 characters.</param>
public sealed record ActivityEntry(string Action)
{
    /// <summary>The kind of thing it happened to, such as a content type or <c>site</c>.</summary>
    public string? TargetType { get; init; }

    /// <summary>The id of the thing it happened to.</summary>
    public string? TargetId { get; init; }

    /// <summary>A name for the target that a reader of the log may see, such as a site slug. Not a field of content.</summary>
    public string? TargetLabel { get; init; }

    /// <summary>The locale the action is about, when it is about one.</summary>
    public string? Locale { get; init; }

    /// <summary>
    /// More facts, by name. Values that look like secrets are masked and long values are cut, and a name that
    /// suggests a secret has its value masked whatever it holds.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Details { get; init; }

    /// <summary>The network address the action came from, when the writer knows it.</summary>
    public string? Ip { get; init; }
}
