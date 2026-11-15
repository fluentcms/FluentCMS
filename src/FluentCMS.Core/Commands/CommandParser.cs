namespace FluentCMS.Core.Commands;

internal sealed record ParsedCommand(HostCommand? Command, CommandArguments? Arguments, bool Help, string? Error);

// Reads the words after "fluentcms". Core takes no dependency on the tool's parser; this one only knows the commands of
// the registry, their options as "--name value" or "--name=value", and --help.
internal static class CommandParser
{
    public static ParsedCommand Parse(IReadOnlyList<HostCommand> commands, IReadOnlyList<string> args)
    {
        var command = commands
            .Where(candidate => Names(candidate, args))
            .MaxBy(candidate => candidate.Name.Length);

        if (command is null)
        {
            var words = string.Join(' ', args.TakeWhile(argument => !argument.StartsWith('-')));
            return new ParsedCommand(null, null, args.Any(IsHelp), words.Length == 0 ? "no command was given" : $"'{words}' is not a command that this host runs");
        }

        // --help, -h and -? are help where an option or an argument stands, and the value of an option that takes one, which
        // is how the tool hands on "--name=-h". The first error is kept, and help after it is still help.
        var rest = args.Skip(command.Name.Split(' ').Length).ToList();
        var help = false;
        string? error = null;
        var positional = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < rest.Count; i++)
        {
            var token = rest[i];
            if (IsHelp(token))
            {
                help = true;
                continue;
            }

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(token);
                continue;
            }

            var separator = token.IndexOf('=', StringComparison.Ordinal);
            var name = separator < 0 ? token : token[..separator];
            var option = command.Options.FirstOrDefault(candidate => candidate.Name == name);
            string? value = null;
            if (option is { TakesValue: true })
            {
                value = separator >= 0 ? token[(separator + 1)..]
                    : i + 1 < rest.Count && !rest[i + 1].StartsWith("--", StringComparison.Ordinal) ? rest[++i]
                    : null;
            }

            error ??= option is null ? $"{name} is not an option of 'fluentcms {command.Name}'"
                : options.ContainsKey(name) ? $"{name} is given more than once"
                : !option.TakesValue && separator >= 0 ? $"{name} takes no value"
                : option.TakesValue && value is null ? $"{name} needs a value"
                : null;
            options[name] = value;
        }

        if (error is null && positional.Count != command.Arguments.Count)
        {
            error = positional.Count > command.Arguments.Count
                ? $"'{positional[command.Arguments.Count]}' is not an argument of 'fluentcms {command.Name}'"
                : $"'fluentcms {command.Name}' needs <{command.Arguments[positional.Count]}>";
        }

        return error is null ? new ParsedCommand(command, new CommandArguments(positional, options), help, null) : new ParsedCommand(command, null, help, error);
    }

    private static bool IsHelp(string argument) => argument is "--help" or "-h" or "-?";

    private static bool Names(HostCommand command, IReadOnlyList<string> args)
    {
        var words = command.Name.Split(' ');
        return args.Count >= words.Length && words.Select((word, index) => args[index] == word).All(match => match);
    }
}
