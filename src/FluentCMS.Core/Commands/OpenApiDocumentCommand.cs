using System.Text;
using FluentCMS.Core.Api.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace FluentCMS.Core.Commands;

// The OpenAPI document as the document service builds it, without listening and without the database, for the tool's
// `generate openapi` in a host project. Written to a file of the tool's, which keeps it apart from anything else the host
// prints. Hidden: the tool's command of another name hands this one on.
internal sealed class OpenApiDocumentCommand : HostCommand
{
    public const string Output = "--output";

    public override string Name => "openapi document";

    public override string Description => "Writes the OpenAPI document of the Delivery and Management APIs to a file, for the fluentcms tool.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Output, "The file to write.", "file"),
    ];

    public override bool Hidden => true;

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var path = context.Arguments.Value(Output)
            ?? throw new CommandException(CommandErrorCodes.BadUsage, $"{Output} names the file to write.", CommandExitCodes.BadUsage);

        var provider = context.Services.GetKeyedService<IOpenApiDocumentProvider>(OpenApiRegistration.DocumentName)
            ?? throw new CommandException(GenerateErrorCodes.OpenApiUnavailable, $"the host has no OpenAPI document '{OpenApiRegistration.DocumentName}'; AddFluentCms registers it.");

        var document = await provider.GetOpenApiDocumentAsync(cancellationToken);
        using var text = new StringWriter();
        document.SerializeAsV31(new OpenApiJsonWriter(text));

        try
        {
            await File.WriteAllTextAsync(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CommandException(CommandErrorCodes.OutputNotWritten, $"{path} could not be written: {exception.Message}");
        }

        return CommandExitCodes.Success;
    }
}

// The 337x codes of the generate commands; the tool has the others of the range.
internal static class GenerateErrorCodes
{
    public const string OpenApiUnavailable = "FCMS3379";
}
