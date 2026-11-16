namespace FluentCMS.Admin.Forms;

/// <summary>
/// What a draft of an unsaved form belongs to: the site, the content type, the entry and the locale it edits. The store adds
/// the signed-in user, so a draft is never found for another user.
/// </summary>
/// <param name="Site">The site the form was read in, as the form sends it in <c>X-Cms-Site</c>; null for the default site.</param>
/// <param name="Type">The name of the content type.</param>
/// <param name="Entry">The id of the entry; null for an entry that is not created yet.</param>
/// <param name="Locale">The locale the form edits, as it sends it in <c>X-Cms-Locale</c>; null for the default locale.</param>
public sealed record FormDraftKey(string? Site, string Type, Guid? Entry, string? Locale);
