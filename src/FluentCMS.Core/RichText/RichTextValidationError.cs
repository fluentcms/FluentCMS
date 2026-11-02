namespace FluentCMS.Core.ProseMirror;

public sealed record RichTextValidationError(string Path, string Message)
{
    public override string ToString() => $"{Path}: {Message}";
}

public sealed class RichTextValidationException : FormatException
{
    public RichTextValidationException(IReadOnlyList<RichTextValidationError> errors)
        : base(Describe(errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<RichTextValidationError> Errors { get; }

    private static string Describe(IReadOnlyList<RichTextValidationError> errors) =>
        errors.Count == 0
            ? "The rich text document is not valid."
            : "The rich text document is not valid. " + string.Join(' ', errors.Select(error => error.ToString()));
}
