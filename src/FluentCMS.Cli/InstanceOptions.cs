using System.CommandLine;

namespace FluentCMS.Cli;

// A running instance and the management API key that reaches it (10.3, P3).
internal sealed record Instance(Uri Url, string Token)
{
    // A key sent over plain http to another machine can be read on the way; on this machine it is a local instance, such as
    // one in development.
    public bool SendsKeyWithoutTls => Url.Scheme == Uri.UriSchemeHttp && !Url.IsLoopback;

    // The key is never written out.
    public override string ToString() => Url.ToString();
}

// The options of the commands that also work against a running instance: --url, and the key in --token or FLUENTCMS_TOKEN.
internal sealed class InstanceOptions
{
    public const string TokenVariable = "FLUENTCMS_TOKEN";
    public const string TokenOption = "--token";

    public Option<string> Url { get; } = new("--url") { Description = "The address of a running instance, such as https://cms.example.com, in place of the project.", HelpName = "url" };

    public Option<string> Token { get; } = new(TokenOption) { Description = $"A management API key for --url. {TokenVariable} when left out, which keeps it out of the process list.", HelpName = "key" };

    public static InstanceOptions AddTo(Command command)
    {
        var options = new InstanceOptions();
        command.Add(options.Url);
        command.Add(options.Token);
        return options;
    }

    // As Resolve, and a key that goes over plain http to another machine is warned about on standard error, once per command.
    public Instance? Resolve(ParseResult result, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var instance = Resolve(result);
        if (instance is { SendsKeyWithoutTls: true })
        {
            error.WriteLine($"{CliErrorCodes.KeyWithoutTls}: {instance} is http, so the key is sent without TLS; use https outside a trusted network.");
        }

        return instance;
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
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new CliException(CliErrorCodes.InvalidInstance, $"--url needs a management API key in --token or {TokenVariable}.", ExitCodes.BadUsage);
        }

        // A header cannot carry it, and HttpClient would refuse it with an exception whose message repeats it.
        token = token.Trim();
        return token.Any(character => character is < '!' or > '~')
            ? throw new CliException(CliErrorCodes.InvalidInstance, "the key holds a character that no API key has; the value is not repeated here.", ExitCodes.BadUsage)
            : new Instance(uri, token);
    }
}
