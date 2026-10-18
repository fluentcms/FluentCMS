using System.Globalization;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Results;

namespace FluentCMS.Admin.Audit;

public sealed record AuditExportRequest(AuditQuery Query, AuditExportFormat Format);

// The export form as the audit page posts it to the host's download endpoint, which sends a rejected one back with ReturnQuery.
public sealed class AuditExportForm
{
    public const string FromInvalid = "audit_from_invalid";
    public const string ToInvalid = "audit_to_invalid";
    public const string SubjectIdInvalid = "audit_subject_id_invalid";

    // The query names the page reads a rejected form back from.
    public const string FromName = "from";
    public const string ToName = "to";
    public const string SubjectTypeName = "subjectType";
    public const string SubjectIdName = "subjectId";
    public const string ErrorName = "error";

    // UTC dates as a date input sends them, both included.
    public string? From { get; set; }

    public string? To { get; set; }

    public string? SubjectType { get; set; }

    public string? SubjectId { get; set; }

    public string? Format { get; set; }

    public Result<AuditExportRequest> ToRequest()
    {
        var errors = new List<Error>();

        var from = ReadDate(From, FromInvalid, errors);
        var to = ReadDate(To, ToInvalid, errors);
        if (from > to)
        {
            errors.Add(new Error(AuditErrorCodes.WindowInverted, "The end date is before the start date."));
        }

        var subject = ReadSubject(errors);
        var format = ReadFormat(errors);

        if (errors.Count > 0)
        {
            return Result<AuditExportRequest>.Failure(errors);
        }

        var query = new AuditQuery
        {
            From = from is { } start ? Midnight(start) : null,
            To = to is { } end && end < DateOnly.MaxValue ? Midnight(end.AddDays(1)) : null,
            Subject = subject,
        };

        var valid = query.Validate();
        return valid.IsSuccess
            ? Result<AuditExportRequest>.Success(new AuditExportRequest(query, format!.Value))
            : Result<AuditExportRequest>.Failure(valid.Errors);
    }

    public IEnumerable<KeyValuePair<string, string?>> ReturnQuery(IEnumerable<Error> errors)
    {
        (string Name, string? Value)[] fields =
        [
            (FromName, From),
            (ToName, To),
            (SubjectTypeName, SubjectType),
            (SubjectIdName, SubjectId),
        ];

        return fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Value))
            .Select(field => KeyValuePair.Create(field.Name, field.Value))
            .Concat(errors.Select(error => error.Code).Distinct().Select(code => KeyValuePair.Create(ErrorName, (string?)code)));
    }

    private static DateOnly? ReadDate(string? text, string code, List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors.Add(new Error(code, $"'{text}' is not a date in the form YYYY-MM-DD."));
        return null;
    }

    private AuditSubject? ReadSubject(List<Error> errors)
    {
        var type = SubjectType?.Trim();
        var id = SubjectId?.Trim();

        if (string.IsNullOrEmpty(type) && string.IsNullOrEmpty(id))
        {
            return null;
        }

        if (string.IsNullOrEmpty(type))
        {
            errors.Add(new Error(AuditErrorCodes.SubjectTypeMissing, "An item's history needs the item's type."));
        }

        if (string.IsNullOrEmpty(id))
        {
            errors.Add(new Error(AuditErrorCodes.SubjectIdMissing, "An item's history needs the item's id."));
            return null;
        }

        if (!Guid.TryParse(id, out var subjectId))
        {
            errors.Add(new Error(SubjectIdInvalid, $"'{id}' is not an item id."));
            return null;
        }

        return new AuditSubject(type ?? string.Empty, subjectId);
    }

    private AuditExportFormat? ReadFormat(List<Error> errors)
    {
        switch (Format?.Trim().ToLowerInvariant())
        {
            case "json":
                return AuditExportFormat.Json;
            case "csv":
                return AuditExportFormat.Csv;
            default:
                errors.Add(new Error(AuditErrorCodes.ExportFormatUnknown, "Choose JSON or CSV."));
                return null;
        }
    }

    private static DateTimeOffset Midnight(DateOnly date)
    {
        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }
}
