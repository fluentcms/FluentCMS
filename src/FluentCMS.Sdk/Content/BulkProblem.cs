namespace FluentCMS.Content;

/// <summary>
/// Why one entry of a <see cref="BulkRequest"/> failed, as data: the code and message of the problem the single call would
/// have answered, without the exception, its stack trace or its inner exceptions.
/// </summary>
/// <param name="Code">
/// The stable code, the one the single call's problem details carry: <c>FCMS3104</c> for an entry that does not exist or that
/// the principal may not reach, <c>FCMS3103</c> for one it may not change, <c>FCMS3105</c> for a version conflict, the code of a
/// <see cref="Hooks.HookRejectedException"/>, the code of the one error or <c>FCMS2100</c> for a <see cref="ValidationException"/>,
/// and <c>FCMS3199</c> for a failure that says nothing of its cause, which the host logs.
/// </param>
/// <param name="Message">A message for people.</param>
public sealed record BulkProblem(string Code, string Message)
{
    /// <summary>The problems found when the entry did not validate; empty for any other failure.</summary>
    public IReadOnlyList<ValidationError> Errors { get; init; } = [];

    /// <summary>For a version conflict, the stored entry without what the principal may not read, or <see langword="null"/>.</summary>
    public ContentDocument? Current { get; init; }
}
