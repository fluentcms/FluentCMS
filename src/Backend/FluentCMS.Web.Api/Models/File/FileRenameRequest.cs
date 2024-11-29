namespace FluentCMS.Web.Api.Models;

public class FileRenameRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
}

