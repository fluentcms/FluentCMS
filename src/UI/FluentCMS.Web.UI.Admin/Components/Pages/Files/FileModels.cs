namespace FluentCMS.Web.ApiClients;

public class AssetDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string NormalizedName { get; set; }
    public bool IsFolder { get; set; }
    public bool IsParentFolder { get; set; } = false;
    public string? ContentType { get; set; }
    public string? Extension { get; set; }
    public long? Size { get; set; }
}
