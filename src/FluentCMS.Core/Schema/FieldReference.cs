namespace FluentCMS.Core.Schema;

// A field of a definition whose values point at the target.
public sealed record FieldReference(string Field, ReferenceTarget Target);
