using Ardalis.GuardClauses;

namespace FluentCMS.Entities.Sites;
public class Site : IAuditEntity
{
    //todo : create a dto and revert these changes 
    public Site() { }
    public Site(
        Guid id,
        string name,
        string description,
        ICollection<string> urls,
        Guid roleId)
    {
        Guard.Against.NullOrEmpty(urls);
        SetName(name);
        urls.ToList().ForEach(x => AddUrl(x));
        SetDescription(description);
        Urls = new List<string>(urls);
        SetRoleId(roleId);
        Id = id;
    }

    public void SetRoleId(Guid roleId)
    {
        Guard.Against.NullOrEmpty(roleId, nameof(roleId));
        RoleId = roleId;
    }

    public void SetDescription(string description)
    {
        Description = description;
    }

    public void SetName(string name)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Name = name;
    }

    public void AddUrl(string url)
    {
        Guard.Against.NullOrEmpty(url, nameof(url));
        // guard against duplicate
        Guard.Against.InvalidInput(url, nameof(url), x => !Urls.Any(u => u.Equals(x)), "Url already exists");
        Urls.Add(url);
    }
    public void RemoveUrl(string url)
    {
        Guard.Against.NullOrEmpty(url, nameof(url));
        Urls.Remove(url);
    }


    public Guid Id { get; set; } = Guid.Empty;
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = default;
    public string LastUpdatedBy { get; set; } = "";
    public DateTime LastUpdatedAt { get; set; } = default;
    //todo : create a dto and revert these changes 
    public string Name { get;  set; } = "";
    //todo : create a dto and revert these changes 
    public string Description { get;  set; } = "";
    public List<string> Urls { get; set; } = [];
    public Guid RoleId { get;  set; } = Guid.Empty;
}
