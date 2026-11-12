using System.CommandLine;

namespace FluentCMS.Cli;

// The options of every project command.
internal sealed class ProjectOptions
{
    public Option<string> Project { get; } = new("--project") { Description = "The host project, which is built first. The current directory when left out.", HelpName = "path" };

    public Option<string> Assembly { get; } = new("--assembly") { Description = "The host's built assembly; nothing is built, and --project is only the host's directory.", HelpName = "path" };

    public Option<bool> NoBuild { get; } = new("--no-build") { Description = "Runs the host as it was last built." };

    public Option<string> Environment { get; } = new("--environment") { Description = "The environment the host runs in, such as Production.", HelpName = "name" };

    public static ProjectOptions AddTo(Command command)
    {
        var options = new ProjectOptions();
        command.Add(options.Project);
        command.Add(options.Assembly);
        command.Add(options.NoBuild);
        command.Add(options.Environment);
        return options;
    }

    public HostTarget Resolve(ParseResult result, TextWriter output) =>
        HostTarget.Resolve(result.GetValue(Project), result.GetValue(Assembly), result.GetValue(NoBuild), output);

    public string? EnvironmentOf(ParseResult result) => result.GetValue(Environment);
}
