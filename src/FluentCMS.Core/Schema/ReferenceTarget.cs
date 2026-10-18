namespace FluentCMS.Core.Schema;

// Entries of the content type with this slug, or one field of them when Field is set, such as product.price (SCH-04).
public sealed record ReferenceTarget(string Type, string? Field = null);
