using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Plugins.TodoManager.Models;

public class Todo : IEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime? DueDate { get; set; }
}
