namespace FluentCMS.Admin.Components;

/// <summary>An item of a <see cref="CmsBreadcrumb"/>.</summary>
/// <param name="Text">The text shown for the item.</param>
/// <param name="Href">The link of the item, relative to the app's base, or <see langword="null"/> for a step that has no page of its own.</param>
public sealed record CmsBreadcrumbItem(string Text, string? Href = null);
