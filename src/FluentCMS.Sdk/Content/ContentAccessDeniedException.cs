namespace FluentCMS.Content;

/// <summary>Thrown when the principal may not perform an operation on a content type. The HTTP layer maps it to a 401 or a 403.</summary>
public sealed class ContentAccessDeniedException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="type">The name of the content type.</param>
    /// <param name="operation">The name of the operation, such as <c>update</c>.</param>
    public ContentAccessDeniedException(string type, string operation)
        : base($"The operation {operation} on {type} is not allowed.")
    {
        Type = type;
        Operation = operation;
    }

    /// <summary>The name of the content type.</summary>
    public string Type { get; }

    /// <summary>The name of the operation.</summary>
    public string Operation { get; }
}
