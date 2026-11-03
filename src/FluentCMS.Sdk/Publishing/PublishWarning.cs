namespace FluentCMS.Publishing;

/// <summary>Something a reader of a published entry will miss.</summary>
/// <param name="Path">The field that holds the reference.</param>
/// <param name="Code">One of <see cref="PublishWarningCodes"/>.</param>
/// <param name="Message">A description for a person.</param>
/// <param name="TargetType">The type of the target, when the principal may know it.</param>
/// <param name="TargetId">The id of the target, when the principal may know it.</param>
public sealed record PublishWarning(string Path, string Code, string Message, string? TargetType = null, Guid? TargetId = null);
