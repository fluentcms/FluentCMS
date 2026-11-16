namespace FluentCMS.Admin.Components;

/// <summary>An option of a <see cref="CmsSelect"/>.</summary>
/// <param name="Value">The value the select binds to.</param>
/// <param name="Text">The text shown for the option.</param>
public sealed record CmsOption(string Value, string Text);
