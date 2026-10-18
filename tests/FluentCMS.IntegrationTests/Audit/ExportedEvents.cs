using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;

namespace FluentCMS.IntegrationTests.Audit;

// Reads an export back into events the way a third party would, from the documented shape alone.
internal static class ExportedEvents
{
    public static List<AuditEvent> FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.EnumerateArray().Select(element => ToEvent(
            element.EnumerateObject()
                .Where(property => property.Name != "sources")
                .ToDictionary(property => property.Name, property => property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString()),
            element.GetProperty("sources").EnumerateArray().Select(source => source.GetString()!).ToList())).ToList();
    }

    public static List<AuditEvent> FromCsv(string csv)
    {
        var rows = ParseCsv(csv);
        var header = rows[0];

        return rows.Skip(1).Select(row =>
        {
            var fields = header.Select((name, i) => (Name: name!, Value: row[i])).ToDictionary(field => field.Name, field => field.Value);
            var sources = JsonSerializer.Deserialize<List<string>>(fields["sources"]!)!;
            fields.Remove("sources");
            return ToEvent(fields, sources);
        }).ToList();
    }

    // RFC 4180 and the export's two conventions: an unquoted empty cell is null, and one leading ' is not part of the value.
    public static List<List<string?>> ParseCsv(string csv)
    {
        var rows = new List<List<string?>>();
        var row = new List<string?>();
        var cell = new StringBuilder();
        var quoted = false;
        var inQuotes = false;

        void EndCell()
        {
            var text = cell.ToString();
            row.Add(!quoted && text.Length == 0 ? null : text.StartsWith('\'') ? text[1..] : text);
            cell.Clear();
            quoted = false;
        }

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c != '"')
                {
                    cell.Append(c);
                }
                else if (i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = false;
                }
            }
            else if (c == '"')
            {
                inQuotes = quoted = true;
            }
            else if (c == ',')
            {
                EndCell();
            }
            else if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
            {
                EndCell();
                rows.Add(row);
                row = [];
                i++;
            }
            else
            {
                cell.Append(c);
            }
        }

        Assert.False(inQuotes, "The CSV ends inside a quoted cell.");
        Assert.True(row.Count == 0 && cell.Length == 0, "The CSV does not end with a line break.");
        return rows;
    }

    // Compares every field, sources by value, which record equality alone would not do for a list.
    public static (AuditEvent Event, string Sources) Fingerprint(AuditEvent auditEvent)
    {
        return (auditEvent with { Sources = [] }, JsonSerializer.Serialize(auditEvent.Sources));
    }

    private static AuditEvent ToEvent(Dictionary<string, string?> fields, List<string> sources)
    {
        var initiatorId = Guid.Parse(fields["initiatorId"]!);
        Initiator initiator = fields["initiatorKind"] switch
        {
            "user" => new UserInitiator(initiatorId, fields["initiatorName"]!),
            "agent" => new AgentInitiator(initiatorId, fields["initiatorName"]!),
            var kind => throw new InvalidOperationException($"Unknown initiator kind '{kind}'."),
        };

        var auditEvent = new AuditEvent
        {
            Id = Guid.Parse(fields["id"]!),
            Timestamp = DateTimeOffset.Parse(fields["timestamp"]!, CultureInfo.InvariantCulture),
            Initiator = initiator,
            Operation = fields["operation"]!,
            SubjectType = fields["subjectType"],
            SubjectId = ParseId(fields["subjectId"]),
            Model = fields["model"],
            PromptHash = fields["promptHash"],
            Sources = sources,
            ChangeSetId = ParseId(fields["changeSetId"]),
            PreviousVersionId = ParseId(fields["previousVersionId"]),
        };

        Assert.Equal(auditEvent.AgentCredentialId, ParseId(fields["agentCredentialId"]));
        Assert.Equal(14, fields.Count + 1);
        return auditEvent;
    }

    private static Guid? ParseId(string? text)
    {
        return text is null ? null : Guid.Parse(text);
    }
}
