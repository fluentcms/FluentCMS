namespace FluentCMS.Content;

/// <summary>Gives services ambient access to the <see cref="OperationContext"/> of the current operation.</summary>
public interface IOperationContextAccessor
{
    /// <summary>
    /// The context of the current operation, or <see langword="null"/> outside one.
    /// Entry points set it; services read it. Setting <see langword="null"/> clears it.
    /// </summary>
    OperationContext? Current { get; set; }
}
