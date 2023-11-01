using FluentCMS.Entities;
using System.Runtime.Serialization;

namespace FluentCMS.Application;
[Serializable]
internal class EntityNotFoundException<T> : ApplicationException where T : IEntity
{
    private Guid _id;


    public EntityNotFoundException(Guid id) : base($"{nameof(T)} Entity with id {id} not found")
    {
        _id = id;
    }

}