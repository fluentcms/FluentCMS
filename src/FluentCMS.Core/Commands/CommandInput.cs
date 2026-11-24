using System.Text;

namespace FluentCMS.Core.Commands;

// What a command reads: standard input, and a secret typed at the terminal without an echo. A test gives lines of its own.
internal abstract class CommandInput
{
    public static CommandInput None { get; } = new TextCommandInput(TextReader.Null, terminal: false);

    // Whether a person types at a terminal, so that a prompt can be answered.
    public abstract bool IsTerminal { get; }

    public abstract Task<string?> ReadLineAsync(CancellationToken cancellationToken);

    // The prompt goes to standard error, so that standard output stays the command's own.
    public abstract Task<string?> ReadSecretAsync(TextWriter prompt, string label, CancellationToken cancellationToken);
}

internal sealed class TextCommandInput(TextReader reader, bool terminal) : CommandInput
{
    public override bool IsTerminal => terminal;

    public override async Task<string?> ReadLineAsync(CancellationToken cancellationToken) => await reader.ReadLineAsync(cancellationToken);

    public override async Task<string?> ReadSecretAsync(TextWriter prompt, string label, CancellationToken cancellationToken)
    {
        await prompt.WriteAsync(label);
        var secret = await reader.ReadLineAsync(cancellationToken);
        await prompt.WriteLineAsync();
        return secret;
    }
}

internal sealed class ConsoleCommandInput : CommandInput
{
    // One reader for the process, since it buffers what it has read ahead.
    private readonly Lazy<TextReader> _redirected = new(() => Redirected(Console.OpenStandardInput(), Console.InputEncoding));

    public override bool IsTerminal => !Console.IsInputRedirected;

    public override async Task<string?> ReadLineAsync(CancellationToken cancellationToken) =>
        await (Console.IsInputRedirected ? _redirected.Value : Console.In).ReadLineAsync(cancellationToken);

    // Console.In keeps a byte order mark as text, and PowerShell on Windows can write one before what it pipes, which
    // would then be part of a password. Here the mark is dropped, and what follows it is read as the mark says.
    internal static TextReader Redirected(Stream input, Encoding encoding) =>
        new StreamReader(input, encoding, detectEncodingFromByteOrderMarks: true);

    public override async Task<string?> ReadSecretAsync(TextWriter prompt, string label, CancellationToken cancellationToken)
    {
        await prompt.WriteAsync(label);
        await prompt.FlushAsync(cancellationToken);

        var secret = new StringBuilder();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    await prompt.WriteLineAsync();
                    return secret.ToString();
                case ConsoleKey.Backspace:
                    if (secret.Length > 0)
                    {
                        secret.Length--;
                    }

                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        secret.Append(key.KeyChar);
                    }

                    break;
            }
        }
    }
}
