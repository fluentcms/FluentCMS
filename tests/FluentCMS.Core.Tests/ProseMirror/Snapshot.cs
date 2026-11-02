using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Shouldly;

namespace FluentCMS.Core.Tests.ProseMirror;

internal static class Snapshot
{
    private const string UpdateVariable = "FLUENTCMS_UPDATE_SNAPSHOTS";

    public static string Read(string name) => Normalize(File.ReadAllText(OutputPath(name)));

    public static string Compact(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            document.RootElement.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void ShouldMatchSnapshot(this string actual, string name, [CallerFilePath] string callerPath = "")
    {
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            var source = Path.Combine(Path.GetDirectoryName(callerPath)!, "Snapshots", name);
            File.WriteAllText(source, actual + "\n", new UTF8Encoding(false));
        }
        else
        {
            actual.ShouldBe(Read(name));
        }
    }

    private static string OutputPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "ProseMirror", "Snapshots", name);

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
}
