namespace FluentCMS.Plugins.TodoManagement.Models;

public class Todo : AuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime? DueDate { get; set; }
}
