using System.Text;

namespace FluentCMS.Cli.Generate;

// Writes generated files, or with --check compares them with what is on disk and writes nothing. A file that the same
// command generated before, by its header, and that it no longer generates, such as the class of a removed type, is
// removed when writing, before the files are written, so that a class whose name changed only in case keeps its new
// name on a file system that ignores case; it counts as a difference when checking.
internal static class GeneratedFiles
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // The files of the directory that `marker` says a run of the same command wrote.
    public static IReadOnlyList<string> Previous(string directory, string pattern, string marker) =>
        Directory.Exists(directory)
            ? [.. Directory.EnumerateFiles(directory, pattern).Where(path => StartsWithMarker(path, marker)).Select(Path.GetFileName).OfType<string>()]
            : [];

    public static IReadOnlyList<string> Differences(string directory, IReadOnlyList<GeneratedFile> files, IReadOnlyList<string> previous)
    {
        List<string> differences = [];
        foreach (var file in files)
        {
            var path = Path.Combine(directory, file.Name);
            if (!File.Exists(path))
            {
                differences.Add($"{file.Name} is missing");
            }
            else if (File.ReadAllText(path, Utf8) != file.Text)
            {
                differences.Add($"{file.Name} differs");
            }
        }

        differences.AddRange(previous.Except(files.Select(file => file.Name), StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(name => $"{name} is no longer generated"));
        return differences;
    }

    public static void Write(string directory, IReadOnlyList<GeneratedFile> files, IReadOnlyList<string> previous, TextWriter output)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var written = 0;
            foreach (var name in previous.Except(files.Select(file => file.Name), StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                var path = Path.Combine(directory, name);
                File.Delete(path);
                output.WriteLine($"Removed {path}");
                written++;
            }

            foreach (var file in files)
            {
                var path = Path.Combine(directory, file.Name);
                if (File.Exists(path) && File.ReadAllText(path, Utf8) == file.Text)
                {
                    continue;
                }

                File.WriteAllText(path, file.Text, Utf8);
                output.WriteLine($"Wrote {path}");
                written++;
            }

            if (written == 0)
            {
                output.WriteLine($"{directory} is up to date.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CliException(CliErrorCodes.OutputNotWritten, $"{directory} could not be written: {exception.Message}");
        }
    }

    private static bool StartsWithMarker(string path, string marker)
    {
        try
        {
            using var reader = new StreamReader(path, Utf8);
            for (var line = 0; line < 2; line++)
            {
                if (reader.ReadLine() is { } text && text.StartsWith(marker, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
