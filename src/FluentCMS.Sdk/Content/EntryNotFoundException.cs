namespace FluentCMS.Content;

/// <summary>Thrown when an entry does not exist, or when the principal may not reach it, so that a client cannot tell the two apart. The HTTP layer maps it to a 404.</summary>
public sealed class EntryNotFoundException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="type">The name of the content type.</param>
    /// <param name="id">The id of the entry.</param>
    public EntryNotFoundException(string type, Guid id)
        : base($"There is no {type} {id}.")
    {
        Type = type;
        Id = id;
    }

    /// <summary>The name of the content type.</summary>
    public string Type { get; }

    /// <summary>The id of the entry.</summary>
    public Guid Id { get; }
}
