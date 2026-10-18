using System.Text;
using System.Text.Json;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Audit;

internal sealed class AuditExporter(IAuditReader reader) : IAuditExporter
{
    // Output reaches the destination in pieces of about this size, so a long range never builds up in memory.
    private const int ChunkSize = 16 * 1024;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<Result> ExportAsync(
        AuditQuery query,
        AuditExportFormat format,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(destination);

        if (!Enum.IsDefined(format))
        {
            return Result.Failure(
                "audit_export_format_unknown",
                $"The audit log exports as {string.Join(" or ", Enum.GetNames<AuditExportFormat>())}, not as {format}.");
        }

        var read = reader.Read(query);
        if (!read.IsSuccess)
        {
            return Result.Failure(read.Errors);
        }

        if (format == AuditExportFormat.Json)
        {
            await WriteJsonAsync(read.Value, destination, cancellationToken);
        }
        else
        {
            await WriteCsvAsync(read.Value, destination, cancellationToken);
        }

        return Result.Success();
    }

    // An array of objects, every key present.
    private static async Task WriteJsonAsync(IAsyncEnumerable<AuditEvent> events, Stream destination, CancellationToken cancellationToken)
    {
        await using var json = new Utf8JsonWriter(destination, new JsonWriterOptions { Encoder = AuditExportShape.Encoder });

        json.WriteStartArray();
        await foreach (var auditEvent in events.WithCancellation(cancellationToken))
        {
            var values = AuditExportShape.Values(auditEvent);

            json.WriteStartObject();
            for (var i = 0; i < values.Length; i++)
            {
                var name = AuditExportShape.Names[i];
                if (name == AuditExportShape.Sources)
                {
                    json.WritePropertyName(name);
                    json.WriteRawValue(values[i]!);
                }
                else if (values[i] is { } value)
                {
                    json.WriteString(name, value);
                }
                else
                {
                    json.WriteNull(name);
                }
            }

            json.WriteEndObject();

            if (json.BytesPending >= ChunkSize)
            {
                await json.FlushAsync(cancellationToken);
            }
        }

        json.WriteEndArray();
        await json.FlushAsync(cancellationToken);
    }

    // RFC 4180 with a header row and CRLF line ends.
    private static async Task WriteCsvAsync(IAsyncEnumerable<AuditEvent> events, Stream destination, CancellationToken cancellationToken)
    {
        await using var csv = new StreamWriter(destination, Utf8, ChunkSize, leaveOpen: true);
        var line = new StringBuilder();

        await csv.WriteAsync(Row(line, AuditExportShape.Names), cancellationToken);
        await foreach (var auditEvent in events.WithCancellation(cancellationToken))
        {
            await csv.WriteAsync(Row(line, AuditExportShape.Values(auditEvent)), cancellationToken);
        }

        await csv.FlushAsync(cancellationToken);
    }

    private static StringBuilder Row(StringBuilder line, IReadOnlyList<string?> cells)
    {
        line.Clear();
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                line.Append(',');
            }

            AppendCell(line, cells[i]);
        }

        return line.Append("\r\n");
    }

    // Two conventions on top of RFC 4180 keep every value exact: an unquoted empty cell is null and a quoted one is an empty
    // string, and a value a spreadsheet would run as a formula gets one leading ', as does a value already starting with one,
    // so a reader removes exactly one.
    private static void AppendCell(StringBuilder line, string? value)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length == 0)
        {
            line.Append("\"\"");
            return;
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\'')
        {
            value = "'" + value;
        }

        if (value.AsSpan().IndexOfAny(",\"\r\n") >= 0 || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
        {
            line.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        }
        else
        {
            line.Append(value);
        }
    }
}
