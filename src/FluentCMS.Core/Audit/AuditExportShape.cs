using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FluentCMS.Core.Audit;

// The flat shape both export formats share, so a CSV column and a JSON key always carry the same field.
internal static class AuditExportShape
{
    public const string Sources = "sources";

    public static readonly IReadOnlyList<string> Names =
    [
        "id",
        "timestamp",
        "operation",
        "initiatorKind",
        "initiatorId",
        "initiatorName",
        "agentCredentialId",
        "subjectType",
        "subjectId",
        "model",
        "promptHash",
        Sources,
        "changeSetId",
        "previousVersionId",
    ];

    // An export is a file, not HTML, so characters such as ' and + and non-ASCII text are written as they are.
    public static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    private static readonly JsonSerializerOptions SourcesOptions = new() { Encoder = Encoder };

    // One text per name, in the same order, null where the field is empty; sources are always a JSON array of strings.
    public static string?[] Values(AuditEvent auditEvent)
    {
        var (kind, initiatorId) = InitiatorKinds.Describe(auditEvent.Initiator);

        return
        [
            Text(auditEvent.Id),
            auditEvent.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            auditEvent.Operation,
            kind,
            Text(initiatorId),
            auditEvent.Initiator.DisplayName,
            Text(auditEvent.AgentCredentialId),
            auditEvent.SubjectType,
            Text(auditEvent.SubjectId),
            auditEvent.Model,
            auditEvent.PromptHash,
            JsonSerializer.Serialize(auditEvent.Sources, SourcesOptions),
            Text(auditEvent.ChangeSetId),
            Text(auditEvent.PreviousVersionId),
        ];
    }

    private static string? Text(Guid? id)
    {
        return id?.ToString("D", CultureInfo.InvariantCulture);
    }
}
