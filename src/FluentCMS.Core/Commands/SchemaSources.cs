using System.Text;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// The schemas the schema commands read, and how a diff is printed. The fluentcms tool uses the same for --url.
internal static class SchemaSources
{
    public static SchemaModel Host(IServiceProvider services)
    {
        try
        {
            return services.GetRequiredService<SchemaModel>();
        }
        catch (SchemaValidationException exception)
        {
            throw Invalid("the host's schema", exception);
        }
    }

    public static SchemaModel File(string path, IFieldTypeRegistry fieldTypes)
    {
        try
        {
            return SchemaFiles.Read(path, fieldTypes);
        }
        catch (SchemaValidationException exception)
        {
            throw Invalid(path, exception);
        }
        catch (Exception exception) when (SchemaFiles.IsUnreadable(exception))
        {
            throw new CommandException(CommandErrorCodes.SchemaFileUnreadable, $"{path} could not be read: {exception.Message}");
        }
    }

    // 0 when the two are the same and 1 when they differ, so that CI can gate a deploy on it.
    public static async Task<int> WriteDiffAsync(SchemaDiffReport report, bool json, string from, string to, TextWriter output, TextWriter error)
    {
        if (json)
        {
            await output.WriteLineAsync(report.ToJson());
        }
        else
        {
            await report.WriteTextAsync(output);
        }

        if (!report.HasChanges)
        {
            return CommandExitCodes.Success;
        }

        await error.WriteLineAsync($"{CommandErrorCodes.SchemaDrift}: {report.Summary} from {from} to {to}");
        return CommandExitCodes.Failure;
    }

    public static async Task WriteFileAsync(string path, string text, CancellationToken cancellationToken)
    {
        try
        {
            await System.IO.File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new CommandException(CommandErrorCodes.OutputNotWritten, $"{path} could not be written: {exception.Message}");
        }
    }

    private static CommandException Invalid(string what, SchemaValidationException exception) =>
        new(CommandErrorCodes.SchemaInvalid, $"{what} has {Errors(exception.Errors.Count)}:{Environment.NewLine}{string.Join(Environment.NewLine, exception.Errors.Select(error => $"  {error}"))}");

    internal static string Errors(int count) => count == 1 ? "1 error" : $"{count} errors";
}
