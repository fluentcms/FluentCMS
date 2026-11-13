using System.CommandLine;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluentCMS.Cli.Generate;

// `generate typescript`, `generate csharp` and `generate openapi`. They run in the tool: the schema snapshot comes from
// --schema, from an instance with --url, or from the host project through its `schema snapshot`, as the schema tools read
// them. The generated text depends on the snapshot alone. The OpenAPI document of a host comes from the hidden
// `openapi document`.
internal sealed partial class GenerateCommands(
    TextWriter output,
    TextWriter error,
    IHostLauncher launcher,
    Func<Instance, HttpClient> connect,
    Func<ParseResult, ProjectOptions, CancellationToken, Task<string>> hostSnapshot)
{
    private const string OpenApiPath = "openapi/v1.json";

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
        "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "virtual", "void", "volatile", "while", "global",
    };

    public Command Build() => new("generate", "Generates C#, TypeScript or the OpenAPI document from the schema of the host, of an instance or of a file.")
    {
        TypeScript(),
        CSharp(),
        OpenApi(),
    };

    private Command TypeScript()
    {
        var command = new Command("typescript", $"Writes {TypeScriptEmitter.FileName}, one module with the types, the filter and sort builders and createClient over {TypeScriptEmitter.Package}.");
        var options = SchemaOptions.AddTo(command, "The directory to write the module to.", "dir");
        command.SetAction((result, cancellationToken) => GuardAsync(async () =>
        {
            var schema = await ReadSchemaAsync(result, options, cancellationToken);
            return Files(result, options, TypeScriptEmitter.Emit(schema), TypeScriptEmitter.FileName, TypeScriptEmitter.Marker);
        }));
        return command;
    }

    private Command CSharp()
    {
        var @namespace = new Option<string>("--namespace") { Description = "The namespace of the generated classes.", HelpName = "name", Required = true };
        var command = new Command("csharp", "Writes a class per content type, component and block type for FluentCMS.Client, and the filter and sort builders.") { @namespace };
        var options = SchemaOptions.AddTo(command, "The directory to write the files to.", "dir");
        command.SetAction((result, cancellationToken) => GuardAsync(async () =>
        {
            var name = result.GetValue(@namespace)!;
            if (!IsNamespace(name))
            {
                throw new CliException(CliErrorCodes.InvalidNamespace, $"--namespace takes a C# namespace such as MyApp.Content; '{name}' is not one.", ExitCodes.BadUsage);
            }

            var schema = await ReadSchemaAsync(result, options, cancellationToken);
            return Files(result, options, CSharpEmitter.Emit(schema, name), "*.g.cs", CSharpEmitter.Marker);
        }));
        return command;
    }

    private Command OpenApi()
    {
        var file = new Option<string>("--output") { Description = "The file to save the document to.", HelpName = "file", Required = true };
        var check = new Option<bool>("--check") { Description = "Writes nothing; exits with 1 when the file differs from the document." };
        var command = new Command("openapi", "Saves the OpenAPI document of an instance, or of the host project without listening.") { file, check };
        var instance = InstanceOptions.AddTo(command);
        var project = ProjectOptions.AddTo(command);
        command.SetAction((result, cancellationToken) => GuardAsync(async () =>
        {
            var document = instance.Resolve(result) is { } target
                ? await GetAsync(target, OpenApiPath, cancellationToken)
                : await HostDocumentAsync(result, project, cancellationToken);

            var path = Path.GetFullPath(result.GetValue(file)!);
            var directory = Path.GetDirectoryName(path)!;
            var generated = new[] { new GeneratedFile(Path.GetFileName(path), document) };
            return result.GetValue(check) ? Check(directory, generated, []) : Write(directory, generated, []);
        }));
        return command;
    }

    private int Files(ParseResult result, SchemaOptions options, GeneratedCode code, string pattern, string marker)
    {
        foreach (var diagnostic in code.Diagnostics)
        {
            error.WriteLine(diagnostic);
        }

        var directory = Path.GetFullPath(result.GetValue(options.Output)!);
        var previous = GeneratedFiles.Previous(directory, pattern, marker);
        return result.GetValue(options.Check) ? Check(directory, code.Files, previous) : Write(directory, code.Files, previous);
    }

    private int Check(string directory, IReadOnlyList<GeneratedFile> files, IReadOnlyList<string> previous)
    {
        var differences = GeneratedFiles.Differences(directory, files, previous);
        if (differences.Count > 0)
        {
            throw new CliException(CliErrorCodes.OutputStale, $"{directory} is not what the schema generates: {string.Join("; ", differences)}. Run the command again without --check.");
        }

        output.WriteLine($"{directory} is up to date.");
        return ExitCodes.Success;
    }

    private int Write(string directory, IReadOnlyList<GeneratedFile> files, IReadOnlyList<string> previous)
    {
        GeneratedFiles.Write(directory, files, previous, output);
        return ExitCodes.Success;
    }

    private async Task<ClientSchema> ReadSchemaAsync(ParseResult result, SchemaOptions options, CancellationToken cancellationToken)
    {
        var file = result.GetValue(options.Schema);
        var instance = options.Instance.Resolve(result);
        if (file is not null && instance is not null)
        {
            throw new CliException(CliErrorCodes.ConflictingSources, "--schema and --url each name the schema; give one of them.", ExitCodes.BadUsage);
        }

        if (file is not null)
        {
            var path = Path.GetFullPath(file);
            try
            {
                return ClientSchema.Read(await File.ReadAllTextAsync(path, cancellationToken), path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new CliException(CliErrorCodes.SchemaUnreadable, $"{path} could not be read: {exception.Message}");
            }
        }

        if (instance is not null)
        {
            using var http = connect(instance);
            var snapshot = await new InstanceClient(instance, http).GetAsync("schema/snapshot", "schema.read", cancellationToken);
            return ClientSchema.Read(Encoding.UTF8.GetString(snapshot), $"The snapshot of {instance}");
        }

        try
        {
            return ClientSchema.Read(await hostSnapshot(result, options.Project, cancellationToken), "The snapshot of the host");
        }
        catch (CliException exception) when (exception.Code == CliErrorCodes.HostSnapshotFailed)
        {
            throw new CliException(CliErrorCodes.NoHostInput, "the host did not write its schema snapshot, so there is nothing to generate from; it needs the command entry point of FluentCMS.Core and a FluentCMS.Core of this version of the tool.");
        }
    }

    // The host writes the document to a file of the tool's, as it writes its snapshot for the schema tools.
    private async Task<string> HostDocumentAsync(ParseResult result, ProjectOptions project, CancellationToken cancellationToken)
    {
        var target = project.Resolve(result, error);
        var file = Path.Combine(Path.GetTempPath(), $"fluentcms-openapi-{Guid.NewGuid():N}.json");
        try
        {
            using var printed = new StringWriter();
            var exitCode = await launcher.RunAsync(target, project.EnvironmentOf(result), ["openapi", "document", "--output", file], printed, error, cancellationToken);
            if (exitCode != ExitCodes.Success || !File.Exists(file))
            {
                throw new CliException(
                    CliErrorCodes.NoHostInput,
                    $"{target.Name} did not write its OpenAPI document (exit code {exitCode}); it needs the command entry point of FluentCMS.Core and a FluentCMS.Core of this version of the tool.");
            }

            return await File.ReadAllTextAsync(file, cancellationToken);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // A file left in the temporary folder must not hide what failed.
            }
        }
    }

    // The key goes in the Authorization header only, and neither it nor a response body is written out. Only a JSON object
    // with the "openapi" version is the instance's document.
    private async Task<string> GetAsync(Instance instance, string path, CancellationToken cancellationToken)
    {
        using var client = connect(instance);
        var baseUrl = instance.Url.AbsoluteUri.EndsWith('/') ? instance.Url : new Uri(instance.Url.AbsoluteUri + "/");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", instance.Token);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new CliException(CliErrorCodes.InstanceRequestFailed, $"{instance} could not be reached: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CliException(CliErrorCodes.InstanceRequestFailed, $"{instance} did not answer GET {path} within {client.Timeout.TotalSeconds:0} seconds.");
        }

        using (response)
        {
            if (InstanceAnswer.IsRedirect(response))
            {
                throw new CliException(CliErrorCodes.UnexpectedAnswer, $"GET {path} of {instance} answered {InstanceAnswer.Redirect(response)}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new CliException(CliErrorCodes.InstanceRequestFailed, $"GET {path} of {instance} answered {(int)response.StatusCode}{ProblemCode(body)}.");
            }

            if (!InstanceAnswer.Is(response, InstanceAnswer.Json) || !IsOpenApiDocument(body))
            {
                throw new CliException(
                    CliErrorCodes.UnexpectedAnswer,
                    $"GET {path} of {instance} answered {InstanceAnswer.MediaTypeOf(response)} that is not an OpenAPI document, so nothing was written; is it a FluentCMS instance?");
            }

            return body;
        }
    }

    private static bool IsOpenApiDocument(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("openapi", out var version)
                && version.ValueKind == JsonValueKind.String;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ProblemCode(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                ? $" with {code.GetString()}"
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static bool IsNamespace(string name) =>
        NamespacePattern().IsMatch(name) && !name.Split('.').Any(Keywords.Contains);

    private async Task<int> GuardAsync(Func<Task<int>> run)
    {
        try
        {
            return await run();
        }
        catch (Exception exception) when (CommandLine.IsReported(exception))
        {
            await error.WriteLineAsync(exception.Message);
            return exception is CliException cli ? cli.ExitCode : ExitCodes.Failure;
        }
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex NamespacePattern();

    // The options of the commands that read a schema: where it is and where the files go.
    private sealed class SchemaOptions
    {
        public required Option<string> Output { get; init; }

        public required Option<bool> Check { get; init; }

        public required Option<string> Schema { get; init; }

        public required InstanceOptions Instance { get; init; }

        public required ProjectOptions Project { get; init; }

        public static SchemaOptions AddTo(Command command, string output, string outputName)
        {
            var directory = new Option<string>("--output") { Description = output, HelpName = outputName, Required = true };
            var check = new Option<bool>("--check") { Description = "Writes nothing; exits with 1 when the files on disk differ from what the schema generates." };
            var schema = new Option<string>("--schema") { Description = "A schema snapshot file, in place of the project or an instance.", HelpName = "file" };
            command.Add(directory);
            command.Add(check);
            command.Add(schema);
            return new SchemaOptions { Output = directory, Check = check, Schema = schema, Instance = InstanceOptions.AddTo(command), Project = ProjectOptions.AddTo(command) };
        }
    }
}
