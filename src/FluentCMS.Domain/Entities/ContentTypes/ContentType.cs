using Ardalis.GuardClauses;

namespace FluentCMS.Entities.ContentTypes;
public class ContentType : IAuditEntity
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = default;
    public string LastUpdatedBy { get; set; } = "";
    public DateTime LastUpdatedAt { get; set; } = default;
    public string Title { get; protected set; } = "";
    public string Slug { get; protected set; } = "";

    protected ContentType() { }
    public ContentType(Guid id, string title)
    {
        Id = id;
        SetTitle(title);
        SetSlug(title.Trim().ToLower().Replace(" ", "-"));
    }


    public void SetTitle(string title)
    {
        Guard.Against.NullOrEmpty(title);
        Title = title.Trim();
    }

    public void SetSlug(string slug)
    {
        //service should check for duplication and other validations
        Guard.Against.NullOrEmpty(slug);
        Slug = slug;
    }

    //this way we'll make sure that we don't have duplicate field names!

    public ICollection<ContentTypeField> ContentTypeFields { get; set; } = new List<ContentTypeField>();

    public void AddContentTypeField(ContentTypeField field)
    {
        if (ContentTypeFields.Any(x => x.Title == field.Title))
        {
            throw new ApplicationException("Duplicate field name");
        }
        ContentTypeFields.Add(field);
    }
    public void RemoveContentTypeField(ContentTypeField field)
    {
        ContentTypeFields.Remove(field);
    }
}
