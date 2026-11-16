using System.Text.Json.Nodes;

namespace FluentCMS.Admin.Forms;

/// <summary>The unsaved state of a form, written when the session ends and restored after the user signs in again.</summary>
/// <param name="Key">What the draft belongs to.</param>
/// <param name="BaseVersion">
/// The entity tag of the read the form was based on, which the save that follows a restore sends as <c>If-Match</c>, so that
/// a change made meanwhile is a 412 and not overwritten.
/// </param>
/// <param name="Fields">The values of the form's fields, as the form would save them.</param>
public sealed record FormDraft(FormDraftKey Key, string BaseVersion, JsonObject Fields);
