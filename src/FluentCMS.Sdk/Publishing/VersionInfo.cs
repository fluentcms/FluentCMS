using FluentCMS.Content;

namespace FluentCMS.Publishing;

/// <summary>One save of an entry in one locale. The values are read with <see cref="IContentService.GetVersionAsync"/>.</summary>
/// <param name="Number">The number of the version, which is the draft version it was saved as.</param>
/// <param name="Locale">The locale of the version; empty for a type that is not localized.</param>
/// <param name="Status">The status the save left the entry in, or <see cref="EntryStatus.Published"/> once this version has been published.</param>
/// <param name="AuthorId">The id of who the save was made for.</param>
/// <param name="Origin">Where the save came from.</param>
/// <param name="ClientId">The client that made the save, if there was one.</param>
/// <param name="Message">A note on the save, if there is one.</param>
/// <param name="CreatedAt">When the save was made.</param>
public sealed record VersionInfo(int Number, string Locale, EntryStatus Status, Guid AuthorId, Origin Origin, string? ClientId, string? Message, DateTimeOffset CreatedAt);
