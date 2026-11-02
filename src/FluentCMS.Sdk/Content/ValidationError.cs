namespace FluentCMS.Content;

/// <summary>One problem found while validating an entry.</summary>
/// <param name="Path">The path of the field in the API shape, such as <c>videoUrl</c>, <c>seo.metaTitle</c>, <c>sections[2].heading</c> or <c>tags[1]</c>. Empty for a problem with the entry as a whole.</param>
/// <param name="Code">A stable code, <c>FCMS21xx</c> for the built-in rules; see <see cref="ValidationErrorCodes"/>.</param>
/// <param name="Message">A message for people.</param>
public sealed record ValidationError(string Path, string Code, string Message);
