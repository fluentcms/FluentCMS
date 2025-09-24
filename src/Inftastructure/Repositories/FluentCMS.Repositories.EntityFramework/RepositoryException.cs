using System.Text;

namespace FluentCMS.Repositories.EntityFramework;

[Serializable]
public class RepositoryException<TEntity>(string message, Exception? innerException) : EnhancedException("Repository", message, innerException) where TEntity : class, IEntity
{
    public string EntityType { get; } = typeof(TEntity).Name;
    public string? EntityId { get; set; }
    public string? Operation { get; set; }

    public RepositoryException(string message) : this(message, null)
    {
    }

    public static RepositoryException<TEntity> ForOperation(string operation, string message, Exception? innerException = null)
    {
        return new RepositoryException<TEntity>(message, innerException)
        {
            Operation = operation
        };
    }

    public static RepositoryException<TEntity> ForEntityOperation(string operation, Guid entityId, string message, Exception? innerException = null)
    {
        return new RepositoryException<TEntity>(message, innerException)
        {
            Operation = operation,
            EntityId = entityId.ToString()
        };
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Repository Exception for {EntityType}");
        if (!string.IsNullOrEmpty(Operation))
            sb.AppendLine($"Operation: {Operation}");
        if (!string.IsNullOrEmpty(EntityId))
            sb.AppendLine($"Entity ID: {EntityId}");
        sb.AppendLine($"Message: {Message}");
        if (InnerException != null)
            sb.AppendLine($"Inner Exception: {InnerException}");
        return sb.ToString();
    }
}
