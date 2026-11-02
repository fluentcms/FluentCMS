using System.Collections.ObjectModel;

namespace FluentCMS.Core.Schema;

public sealed class SchemaValidationException : Exception
{
    internal SchemaValidationException(IReadOnlyList<SchemaError> errors)
        : base(FormatMessage(errors))
    {
        Errors = new ReadOnlyCollection<SchemaError>([.. errors]);
    }

    public IReadOnlyList<SchemaError> Errors { get; }

    private static string FormatMessage(IReadOnlyList<SchemaError> errors) =>
        $"The schema has {errors.Count} {(errors.Count == 1 ? "error" : "errors")}:{Environment.NewLine}"
        + string.Join(Environment.NewLine, errors.Select(error => $"  {error}"));
}
