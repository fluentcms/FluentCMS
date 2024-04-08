using System.Diagnostics.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.Routing;

namespace FluentCMS.Web.Api.Models.File;

public class FileDetailResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long Size { get; set; } = 0;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Url {get; set;}
}
