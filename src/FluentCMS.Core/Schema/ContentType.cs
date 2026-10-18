using FluentCMS.Core.Results;

namespace FluentCMS.Core.Schema;

// A schema for entries, scoped to a site even while only one exists (SCH-01, SCH-07). Changing the definition makes the next
// version; the id, site and slug stay, because entries and references point at them.
public sealed class ContentType
{
    public const int MaxNameLength = 128;

    private ContentType(Guid id, Guid siteId, string name, string slug, int version, ContentTypeDefinition definition)
    {
        Id = id;
        SiteId = siteId;
        Name = name;
        Slug = slug;
        Version = version;
        Definition = definition;
    }

    public Guid Id { get; }

    public Guid SiteId { get; }

    public string Name { get; }

    public string Slug { get; }

    public int Version { get; }

    public ContentTypeDefinition Definition { get; }

    public static Result<ContentType> Create(
        Guid siteId,
        string name,
        string slug,
        ContentTypeDefinition definition,
        FieldTypeRegistry fieldTypes)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(fieldTypes);

        var errors = new List<Error>();
        var trimmedName = name?.Trim() ?? string.Empty;

        if (siteId == Guid.Empty)
        {
            errors.Add(new Error(SchemaErrorCodes.SiteMissing, "A content type belongs to a site, and the site id is empty."));
        }

        if (trimmedName.Length is 0 or > MaxNameLength)
        {
            errors.Add(new Error(SchemaErrorCodes.NameInvalid, $"The name must not be blank and must be at most {MaxNameLength} characters."));
        }

        if (!SchemaNames.IsSlug(slug))
        {
            errors.Add(new Error(
                SchemaErrorCodes.SlugInvalid,
                $"The slug '{slug}' must be lowercase letters and digits in words joined by single hyphens, "
                + $"at most {SchemaNames.MaxLength} characters, such as 'product-page'."));
        }

        errors.AddRange(definition.Validate(fieldTypes).Errors);

        return errors.Count == 0
            ? Result<ContentType>.Success(new ContentType(Guid.CreateVersion7(), siteId, trimmedName, slug, 1, definition))
            : Result<ContentType>.Failure(errors);
    }

    // Rebuilds a stored type as it was written, without validating again: a type stays readable after a field type it uses is gone.
    public static ContentType Restore(Guid id, Guid siteId, string name, string slug, int version, ContentTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(slug);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        return new ContentType(id, siteId, name, slug, version, definition);
    }

    // The current instance is left as it is, so a failed revision changes nothing.
    public Result<ContentType> Revise(ContentTypeDefinition definition, FieldTypeRegistry fieldTypes)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(fieldTypes);

        var validation = definition.Validate(fieldTypes);

        return validation.IsSuccess
            ? Result<ContentType>.Success(new ContentType(Id, SiteId, Name, Slug, Version + 1, definition))
            : Result<ContentType>.Failure(validation.Errors);
    }
}
