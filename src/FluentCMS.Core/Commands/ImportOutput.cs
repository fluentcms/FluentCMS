using System.Globalization;
using System.Text.Json;

namespace FluentCMS.Core.Commands;

// What `fluentcms import` prints, the same whether the import ran inside the host or at an instance with --url: the
// progress lines of the transfer endpoint on standard error, and its report at the end, as text or as the endpoint's last
// line. The tool uses it for the lines it reads from the endpoint.
internal static class ImportOutput
{
    public const string NotClean = "FCMS3250";

    private static readonly string[] CountNames = ["created", "updated", "skipped", "conflicts", "errors"];

    public static async Task WriteProgressAsync(JsonElement line, TextWriter error)
    {
        if (line.ValueKind != JsonValueKind.Object || !line.TryGetProperty("section", out var section))
        {
            return;
        }

        if (line.TryGetProperty("counts", out var counts))
        {
            await error.WriteLineAsync($"{section.GetString()}: {Counts(counts)}");
        }
        else if (line.TryGetProperty("done", out var done))
        {
            await error.WriteLineAsync($"{section.GetString()}: {done.GetRawText()} done");
        }
    }

    // The line is the endpoint's last, {"report":{..}}. Exits with 1 when the report has a conflict or an error, on a dry
    // run too, so that a pipeline can stop on it.
    public static async Task<int> WriteReportAsync(string line, bool json, TextWriter output, TextWriter error)
    {
        using var document = JsonDocument.Parse(line);
        var report = document.RootElement.GetProperty("report");
        var dryRun = report.TryGetProperty("dryRun", out var flag) && flag.ValueKind == JsonValueKind.True;
        var total = report.GetProperty("total");

        if (json)
        {
            await output.WriteLineAsync(line);
        }
        else
        {
            if (dryRun)
            {
                await output.WriteLineAsync("Dry run: nothing was written.");
            }

            await output.WriteLineAsync($"Total: {Counts(total)}");
            if (report.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Object)
            {
                foreach (var section in sections.EnumerateObject())
                {
                    await output.WriteLineAsync($"  {section.Name}: {Counts(section.Value)}");
                }
            }

            if (report.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray().Where(item => Text(item, "outcome") is "conflict" or "error"))
                {
                    await output.WriteLineAsync($"{Text(item, "outcome")} {Text(item, "section")} {Text(item, "id")}: {Text(item, "code")} {Text(item, "message")}".TrimEnd());
                }
            }

            if (Number(report, "omitted") is > 0 and var omitted)
            {
                await output.WriteLineAsync($"{Count(omitted, "more conflict or error is", "more conflicts and errors are")} counted and not listed.");
            }
        }

        var conflicts = Number(total, "conflicts");
        var errors = Number(total, "errors");
        if (conflicts + errors == 0)
        {
            return 0;
        }

        await error.WriteLineAsync(
            $"{NotClean}: the import reported {Count(conflicts, "conflict", "conflicts")} and {Count(errors, "error", "errors")}{(dryRun ? "; it was a dry run, so nothing was written" : "")}.");
        return 1;
    }

    private static string Counts(JsonElement counts) =>
        string.Join(", ", CountNames.Select(name => $"{Number(counts, name).ToString(CultureInfo.InvariantCulture)} {name}"));

    private static int Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static string Count(int count, string one, string many) => $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? one : many)}";
}
