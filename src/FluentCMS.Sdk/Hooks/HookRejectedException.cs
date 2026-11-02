namespace FluentCMS.Hooks;

/// <summary>
/// Thrown when a hook cancels an operation, or by a hook that rejects it directly. The transaction rolls back and
/// the HTTP layer maps the exception to a 422 problem details response with the code and the message.
/// </summary>
public sealed class HookRejectedException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="code">A stable code; plugins should use their own prefix.</param>
    /// <param name="message">A message for people.</param>
    public HookRejectedException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(message);
        Code = code;
    }

    /// <summary>The code the hook gave.</summary>
    public string Code { get; }
}
