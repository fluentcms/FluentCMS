namespace FluentCMS.Exceptions;

[Serializable]
public class EntityNotFoundException<T>(string id) : EnhancedException("NotFound", $"Entity {typeof(T).Name} with id {id} not found")
{
    public EntityNotFoundException() : this(string.Empty)
    {
    }

    public EntityNotFoundException(Guid id) : this(id.ToString())
    {
    }
}
