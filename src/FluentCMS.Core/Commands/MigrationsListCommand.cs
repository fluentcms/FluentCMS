using System.Text.Json;
using FluentCMS.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

internal sealed class MigrationsListCommand : HostCommand
{
    public const string Json = "--json";

    public override string Name => "migrations list";

    public override string Description => "Lists every stream with its applied and pending migrations, in the order they apply.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Json, "Writes the list as one line of JSON: {\"streams\":[{\"name\",\"applied\",\"pending\"}]}."),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var streams = await context.Services.GetRequiredService<MigrationRunner>().ListAsync(cancellationToken);
        if (context.Arguments.Has(Json))
        {
            await context.Output.WriteLineAsync(ToJson(streams));
            return CommandExitCodes.Success;
        }

        foreach (var stream in streams)
        {
            await context.Output.WriteLineAsync(stream.Stream);
            if (stream.Applied.Count == 0 && stream.Pending.Count == 0)
            {
                await context.Output.WriteLineAsync("  no migrations");
            }

            foreach (var id in stream.Applied)
            {
                await context.Output.WriteLineAsync($"  applied  {id}");
            }

            foreach (var id in stream.Pending)
            {
                await context.Output.WriteLineAsync($"  pending  {id}");
            }
        }

        return CommandExitCodes.Success;
    }

    // The form the tool reads when `migrations remove` asks whether the newest content migration is applied.
    private static string ToJson(IReadOnlyList<StreamMigrations> streams)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteStartArray("streams");
            foreach (var stream in streams)
            {
                json.WriteStartObject();
                json.WriteString("name", stream.Stream);
                WriteArray(json, "applied", stream.Applied);
                WriteArray(json, "pending", stream.Pending);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteArray(Utf8JsonWriter json, string name, IReadOnlyList<string> values)
    {
        json.WriteStartArray(name);
        foreach (var value in values)
        {
            json.WriteStringValue(value);
        }

        json.WriteEndArray();
    }
}
