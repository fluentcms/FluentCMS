using Ardalis.GuardClauses;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Entities.Pages;
public class Page:IAuditEntity
{
    public Page(Guid id,Guid siteId, string path, Guid? parentId, string title, int order)
    {
        Guard.Against.NullOrEmpty(id);
        Guard.Against.NullOrEmpty(siteId);
        Id = id;
        SiteId = siteId;
        SetPath(path);
        SetParentId(parentId);
        SetTitle(title);
        SetOrder(order);
    }
    public Page()
    {
        
    }

    public void SetOrder(int order)
    {
        Guard.Against.NullOrOutOfRange(order, nameof(order), 0, int.MaxValue);
        Order = order;
    }

    public void SetTitle(string title)
    {
        Guard.Against.NullOrWhiteSpace(title);
        Title = title;
    }

    public void SetParentId(Guid? parentId)
    {
        ParentId = parentId;
    }

    public void SetPath(string path)
    {
        Guard.Against.NullOrWhiteSpace(path);
        Path = path;
    }

    public Guid SiteId { get; set; } = default;
    public string Path { get; set; } = "";
    public Guid? ParentId { get; set; } = null;
    public string Title { get; set; } = "";
    public int Order { get; set; } = 0;
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = default;
    public string LastUpdatedBy { get; set; } = "";
    public DateTime LastUpdatedAt { get; set; } = default;
    public Guid Id { get; set; } = default;
}
