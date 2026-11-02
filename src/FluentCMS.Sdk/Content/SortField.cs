namespace FluentCMS.Content;

/// <summary>One sort key of a <see cref="ContentQuery"/>.</summary>
/// <param name="Path">The dotted path of the field in the API shape.</param>
/// <param name="Descending">Whether the largest value comes first. A missing value sorts lowest.</param>
public sealed record SortField(string Path, bool Descending = false);
