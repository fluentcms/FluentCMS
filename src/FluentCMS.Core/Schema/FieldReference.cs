namespace FluentCMS.Core.Schema;

// A field whose values point at entries of the content type with the target slug.
public sealed record FieldReference(string Field, string Target);
