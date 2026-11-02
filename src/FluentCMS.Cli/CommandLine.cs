namespace FluentCMS.Cli;

// The shell of `fluentcms`: only `migrations add` until the CLI of M6.
internal static class CommandLine
{
    private const int Success = 0;
    private const int Failure = 1;
    private const int BadUsage = 2;

    private const string Usage = """
        Usage: fluentcms migrations add <name> [--project <path>] [--assembly <path>]

        Generates a content migration from the host's schema and provider, in Migrations/Content of the project.

          --project <path>   The host project, which is built first. Defaults to the current directory.
          --assembly <path>  The host's built assembly: nothing is built, and the project is only where the files go.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args is not ["migrations", "add", var name, .. var options] || name.StartsWith('-') || !TryParse(options, out var project, out var assembly))
        {
            error.WriteLine(Usage);
            return BadUsage;
        }

        try
        {
            using var host = HostProject.Open(project, assembly, output);
            foreach (var file in host.AddMigration(name))
            {
                output.WriteLine($"Created {file}");
            }

            return Success;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ArgumentException or BadImageFormatException)
        {
            error.WriteLine(exception.Message);
            return Failure;
        }
    }

    private static bool TryParse(string[] options, out string? project, out string? assembly)
    {
        project = null;
        assembly = null;

        if (options.Length % 2 != 0)
        {
            return false;
        }

        for (var i = 0; i < options.Length; i += 2)
        {
            switch (options[i])
            {
                case "--project":
                    project = options[i + 1];
                    break;
                case "--assembly":
                    assembly = options[i + 1];
                    break;
                default:
                    return false;
            }
        }

        return true;
    }
}
