using Ardalis.GuardClauses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Entities.ContentTypes;
public class ContentType : IAuditEntity
{
    

    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = default;
    public string LastUpdatedBy { get; set; } = "";
    public DateTime LastUpdatedAt { get; set; } = default;
    public Guid Id { get; set; } // protected set?
    public string Title { get; protected set; } = "";
    public string Slug { get; protected set; } = "";

    public ContentType(Guid id, string title)
    {
        Id = id;
        SetTitle(title);
    }
    protected ContentType()
    {

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
    public IDictionary<string,ContentTypeField> ContentTypeFields { get; set; } = new Dictionary<string, ContentTypeField>();
    
}
