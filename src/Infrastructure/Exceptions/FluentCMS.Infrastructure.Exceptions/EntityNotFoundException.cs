namespace FluentCMS.Infrastructure.Exceptions;

public class EntityNotFoundException : EnhancedException
{
    public EntityNotFoundException(string entityName, Guid entityId) : base(ExceptionCodes.EntityNotFound, string.Format("{0} with ID {1} not found.", entityName, entityId))
    {
    }

    public EntityNotFoundException(string entityName) : base(ExceptionCodes.EntityNotFound, string.Format("{0} not found.", entityName))
    {

    }
}

public class EntityNotFoundException<T> : EntityNotFoundException
{
    public EntityNotFoundException(Guid entityId) : base(typeof(T).Name, entityId)
    {
    }
    public EntityNotFoundException() : base(typeof(T).Name)
    {
    }
}
