using System.Globalization;
using System.Text;

namespace FluentCMS.Core.Commands;

// An option of a command: a flag, or a name followed by a value when ValueName is set.
internal sealed record CommandOption(string Name, string Description, string? ValueName = null)
{
    public bool TakesValue => ValueName is not null;
}

// A command that runs inside the host, as the system with Origin.Cli, before any hosted service has started. The tool
// checks the command line with its own parser and hands it on unchanged, so the options here are the same ones.
internal abstract class HostCommand
{
    // The words after "fluentcms", such as "migrations script".
    public abstract string Name { get; }

    public abstract string Description { get; }

    public virtual IReadOnlyList<string> Arguments => [];

    public virtual IReadOnlyList<CommandOption> Options => [];

    public abstract Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken);

    public string Usage
    {
        get
        {
            var usage = new StringBuilder($"Usage: fluentcms {Name}");
            foreach (var argument in Arguments)
            {
                usage.Append(CultureInfo.InvariantCulture, $" <{argument}>");
            }

            foreach (var option in Options)
            {
                usage.Append(option.TakesValue ? $" [{option.Name} <{option.ValueName}>]" : $" [{option.Name}]");
            }

            usage.AppendLine().AppendLine().AppendLine(Description);
            if (Options.Count > 0)
            {
                usage.AppendLine();
                var width = Options.Max(option => Label(option).Length);
                foreach (var option in Options)
                {
                    usage.AppendLine(CultureInfo.InvariantCulture, $"  {Label(option).PadRight(width)}  {option.Description}");
                }
            }

            return usage.ToString().TrimEnd();
        }
    }

    private static string Label(CommandOption option) => option.TakesValue ? $"{option.Name} <{option.ValueName}>" : option.Name;
}

internal sealed class CommandContext(IServiceProvider services, CommandArguments arguments, TextWriter output, TextWriter error, CommandInput? input = null)
{
    public IServiceProvider Services { get; } = services;

    public CommandArguments Arguments { get; } = arguments;

    public CommandInput Input { get; } = input ?? CommandInput.None;

    public TextWriter Output { get; } = output;

    public TextWriter Error { get; } = error;
}

internal sealed class CommandArguments(IReadOnlyList<string> positional, IReadOnlyDictionary<string, string?> options)
{
    public IReadOnlyList<string> Positional { get; } = positional;

    public bool Has(string option) => options.ContainsKey(option);

    public string? Value(string option) => options.GetValueOrDefault(option);
}
