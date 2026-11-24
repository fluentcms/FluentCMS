using System.Text;
using FluentCMS.Core.Commands;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Commands;

public sealed class CommandInputTests
{
    private const string Password = "Pässword-123!";

    public static TheoryData<string> ConsoleEncodings => ["utf-8", "iso-8859-1"];

    // What PowerShell on Windows can pipe: the mark, the text as UTF-8 and a line end, whatever the console's code page.
    [Theory]
    [MemberData(nameof(ConsoleEncodings))]
    public async Task AByteOrderMarkIsNotPartOfTheFirstLine(string console)
    {
        byte[] piped = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Password + "\r\n")];
        using var reader = ConsoleCommandInput.Redirected(new MemoryStream(piped), ConsoleEncoding(console));

        (await reader.ReadLineAsync(TestContext.Current.CancellationToken)).ShouldBe(Password);
    }

    [Theory]
    [MemberData(nameof(ConsoleEncodings))]
    public async Task WithoutAMarkTheConsoleEncodingReadsTheLine(string console)
    {
        var encoding = ConsoleEncoding(console);
        using var reader = ConsoleCommandInput.Redirected(new MemoryStream(encoding.GetBytes(Password + "\n")), encoding);

        (await reader.ReadLineAsync(TestContext.Current.CancellationToken)).ShouldBe(Password);
    }

    // As Console.InputEncoding gives UTF-8: without a preamble of its own.
    private static Encoding ConsoleEncoding(string name) => name == "utf-8" ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) : Encoding.GetEncoding(name);
}
