namespace FluentCMS.Core.Commands;

internal sealed record ParsedCommand(HostCommand? Command, CommandArguments? Arguments, bool Help, string? Error);

// Reads the words after "fluentcms". Core takes no dependency on the tool's parser; this one only knows the commands of
// the registry, their options as "--name value" or "--name=value", and --help.
internal static class CommandParser
{
    public static ParsedCommand Parse(IReadOnlyList<HostCommand> commands, IReadOnlyList<string> args)
    {
        var help = args.Any(argument => argument is "--help" or "-h" or "-?");
        var command = commands
            .Where(candidate => Names(candidate, args))
            .MaxBy(candidate => candidate.Name.Length);

        if (command is null)
        {
            var words = string.Join(' ', args.TakeWhile(argument => !argument.StartsWith('-')));
            return new ParsedCommand(null, null, help, words.Length == 0 ? "no command was given" : $"'{words}' is not a command that this host runs");
        }

        var rest = args.Skip(command.Name.Split(' ').Length).Where(argument => argument is not ("--help" or "-h" or "-?")).ToList();
        var positional = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < rest.Count; i++)
        {
            var token = rest[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(token);
                continue;
            }

            var separator = token.IndexOf('=', StringComparison.Ordinal);
            var name = separator < 0 ? token : token[..separator];
            var option = command.Options.FirstOrDefault(candidate => candidate.Name == name);
            if (option is null)
            {
                return Refuse(command, help, $"{name} is not an option of 'fluentcms {command.Name}'");
            }

            if (options.ContainsKey(name))
            {
                return Refuse(command, help, $"{name} is given more than once");
            }

            if (!option.TakesValue)
            {
                if (separator >= 0)
                {
                    return Refuse(command, help, $"{name} takes no value");
                }

                options[name] = null;
            }
            else if (separator >= 0)
            {
                options[name] = token[(separator + 1)..];
            }
            else if (i + 1 < rest.Count && !rest[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[name] = rest[++i];
            }
            else
            {
                return Refuse(command, help, $"{name} needs a value");
            }
        }

        if (positional.Count != command.Arguments.Count)
        {
            return Refuse(
                command,
                help,
                positional.Count > command.Arguments.Count
                    ? $"'{positional[command.Arguments.Count]}' is not an argument of 'fluentcms {command.Name}'"
                    : $"'fluentcms {command.Name}' needs <{command.Arguments[positional.Count]}>");
        }

        return new ParsedCommand(command, new CommandArguments(positional, options), help, null);
    }

    private static bool Names(HostCommand command, IReadOnlyList<string> args)
    {
        var words = command.Name.Split(' ');
        return args.Count >= words.Length && words.Select((word, index) => args[index] == word).All(match => match);
    }

    private static ParsedCommand Refuse(HostCommand command, bool help, string error) => new(command, null, help, error);
}
