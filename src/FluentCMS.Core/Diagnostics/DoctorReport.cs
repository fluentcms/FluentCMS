using System.Globalization;
using System.Text.Json;
using FluentCMS.Core.Commands;
using FluentCMS.Diagnostics;

namespace FluentCMS.Core.Diagnostics;

// What `fluentcms doctor` prints, for a host that has not started and for an instance's diagnostics alike: the startup
// report, as text or as the JSON of /system/diagnostics, then on standard error why the host would not start, what is
// pending and whether the FluentCMS assemblies disagree. Exit code 1 for the first two.
internal static class DoctorReport
{
    public static async Task<int> WriteAsync(StartupReport report, IReadOnlyList<string> refusals, bool json, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(report, StartupReportJson.Options));
        }
        else
        {
            await WriteTextAsync(report, output);
        }

        var exitCode = CommandExitCodes.Success;
        foreach (var refusal in refusals)
        {
            await error.WriteLineAsync($"{CommandErrorCodes.HostWouldNotStart}: the host would not start: {refusal}");
            exitCode = CommandExitCodes.Failure;
        }

        if (report.Failure is { } failure)
        {
            await error.WriteLineAsync($"{CommandErrorCodes.HostWouldNotStart}: the report is incomplete ({failure}), so whether the host starts is not known; the log has the exception");
            exitCode = CommandExitCodes.Failure;
        }

        if (report.PendingMigrations.Count > 0)
        {
            await error.WriteLineAsync($"{CommandErrorCodes.MigrationsPending}: {MigrateCommand.Count(report.PendingMigrations.Count)} pending; run 'fluentcms migrate' to apply them");
            exitCode = CommandExitCodes.Failure;
        }

        if (report.Assemblies.Select(assembly => assembly.Version).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            var versions = string.Join(", ", report.Assemblies.Select(assembly => $"{assembly.Name} {assembly.Version}"));
            await error.WriteLineAsync($"{CommandErrorCodes.MixedVersions}: warning: the FluentCMS assemblies have different versions ({versions}); update them together");
        }

        return exitCode;
    }

    public static async Task WriteTextAsync(StartupReport report, TextWriter output)
    {
        var provider = report.DataProvider;
        await output.WriteLineAsync($"FluentCMS {report.Version} in {report.Environment}");
        await output.WriteLineAsync($"Data provider: {provider.Provider} {provider.ProviderVersion}, migrate on startup {(provider.MigrateOnStartup ? "on" : "off")}, schema sync {Or(provider.SchemaSync, "unknown")}");

        await ListAsync(output, "Assemblies", report.Assemblies.Select(assembly => $"{assembly.Name} {assembly.Version}"));
        await ListAsync(output, "Plugins", report.Plugins.Select(plugin => $"{plugin.Name} {plugin.Version}"));
        await ListAsync(output, "Provider slots", report.ProviderSlots.Select(slot => $"{slot.Slot}: {slot.Implementation ?? "none"} ({slot.Source}{(slot.Plugin is null ? "" : $", {slot.Plugin}")})"));
        await ListAsync(output, "Pending migrations", report.PendingMigrations.Select(migration => $"{migration.Stream}  {migration.Id}"));

        var schema = report.Schema;
        await output.WriteLineAsync("Schema:");
        await output.WriteLineAsync($"  hash {Or(schema.Hash, "unknown")}");
        await output.WriteLineAsync(schema.AppliedHash is null
            ? "  not in the schema history"
            : string.Create(CultureInfo.InvariantCulture, $"  applied {schema.AppliedHash} on {schema.AppliedAt:u} by FluentCMS {schema.AppliedVersion}"));
        if (schema.Drifted)
        {
            await output.WriteLineAsync($"  drift: {schema.Changes}");
        }
        else if (schema.Changes is { } note)
        {
            await output.WriteLineAsync($"  {note}");
        }

        await ListAsync(output, "Insecure defaults", report.InsecureDefaults.Select(item => $"{item.Id}: {item.Message}"));
    }

    private static async Task ListAsync(TextWriter output, string title, IEnumerable<string> lines)
    {
        await output.WriteLineAsync($"{title}:");
        var any = false;
        foreach (var line in lines)
        {
            await output.WriteLineAsync($"  {line}");
            any = true;
        }

        if (!any)
        {
            await output.WriteLineAsync("  none");
        }
    }

    private static string Or(string value, string fallback) => value.Length == 0 ? fallback : value;
}
