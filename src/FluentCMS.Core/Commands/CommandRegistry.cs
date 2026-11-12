using System.Globalization;
using System.Text;

namespace FluentCMS.Core.Commands;

// The commands a host runs. Internal: a contract for the commands of plugins is not part of 1.0.
internal static class CommandRegistry
{
    public static IReadOnlyList<HostCommand> Commands { get; } =
    [
        new MigrateCommand(),
        new MigrationsListCommand(),
        new MigrationsScriptCommand(),
        new SchemaSnapshotCommand(),
        new SchemaValidateCommand(),
        new SchemaDiffCommand(),
        new DoctorCommand(),
    ];

    public static string Usage => UsageOf(Commands);

    public static string UsageOf(IReadOnlyList<HostCommand> commands)
    {
        var usage = new StringBuilder("Usage: fluentcms <command> [options]").AppendLine().AppendLine().AppendLine("Commands this host runs:");
        var width = commands.Max(command => command.Name.Length);
        foreach (var command in commands)
        {
            usage.AppendLine(CultureInfo.InvariantCulture, $"  {command.Name.PadRight(width)}  {command.Description}");
        }

        return usage.AppendLine().Append("'fluentcms <command> --help' describes a command.").ToString();
    }
}
