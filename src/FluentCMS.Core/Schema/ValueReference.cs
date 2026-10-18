namespace FluentCMS.Core.Schema;

// One reference a stored value makes (CNT-03, CNT-05): to an entry of the content type in Target, to one field of an entity when
// Target names a field, or to a media asset, which has no Target. Pointer is the JSON Pointer (RFC 6901) of its place within the
// value it was read from, "" for the value itself.
public sealed record ValueReference(string Pointer, ReferenceKind Kind, Guid TargetId, ReferenceTarget? Target = null);

public enum ReferenceKind
{
    Entry,
    Media,
}
