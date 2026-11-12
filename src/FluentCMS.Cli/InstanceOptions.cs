using System.CommandLine;

namespace FluentCMS.Cli;

// A running instance and the management API key that reaches it (10.3, P3).
internal sealed record Instance(Uri Url, string Token)
{
    // The key is never written out.
    public override string ToString() => Url.ToString();
}

// The options of the commands that also work against a running instance: --url, and the key in --token or FLUENTCMS_TOKEN.
internal sealed class InstanceOptions
{
    public const string TokenVariable = "FLUENTCMS_TOKEN";

    public Option<string> Url { get; } = new("--url") { Description = "The address of a running instance, such as https://cms.example.com, in place of the project.", HelpName = "url" };

    public Option<string> Token { get; } = new("--token") { Description = $"A management API key for --url. {TokenVariable} when left out, which keeps it out of the process list.", HelpName = "key" };

    public static InstanceOptions AddTo(Command command)
    {
        var options = new InstanceOptions();
        command.Add(options.Url);
        command.Add(options.Token);
        return options;
    }

    // Null when --url is left out, which is project mode.
    public Instance? Resolve(ParseResult result, Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var url = result.GetValue(Url);
        if (url is null)
        {
            return result.GetValue(Token) is null
                ? null
                : throw new CliException(CliErrorCodes.InvalidInstance, "--token is for --url, which is not given.", ExitCodes.BadUsage);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            throw new CliException(CliErrorCodes.InvalidInstance, "--url takes an http or https address with no user, query or fragment; the value is not repeated here, since it may hold a secret.", ExitCodes.BadUsage);
        }

        var token = result.GetValue(Token) ?? (environment ?? System.Environment.GetEnvironmentVariable)(TokenVariable);
        return string.IsNullOrWhiteSpace(token)
            ? throw new CliException(CliErrorCodes.InvalidInstance, $"--url needs a management API key in --token or {TokenVariable}.", ExitCodes.BadUsage)
            : new Instance(uri, token.Trim());
    }
}
