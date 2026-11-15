using System.Text;

namespace FluentCMS.Cli.Generate;

// Writes generated files, or with --check compares them with what is on disk and writes nothing. A file that the same
// command generated before, by its header, and that it no longer generates, such as the class of a removed type, is
// removed when writing, before the files are written, so that a class whose name changed only in case keeps its new
// name on a file system that ignores case; it counts as a difference when checking. A file of C# that names another
// namespace is another run's, so it is neither removed nor written over: two namespaces need two folders.
internal static class GeneratedFiles
{
    private const string NamespaceKeyword = "namespace ";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // The files of the directory that `marker` says a run of the same command wrote.
    public static IReadOnlyList<string> Previous(string directory, string pattern, string marker) =>
        Directory.Exists(directory)
            ? [.. Directory.EnumerateFiles(directory, pattern).Where(path => StartsWithMarker(path, marker)).Select(Path.GetFileName).OfType<string>()]
            : [];

    public static IReadOnlyList<string> Differences(string directory, IReadOnlyList<GeneratedFile> files, IReadOnlyList<string> previous)
    {
        RefuseNamesThatDifferInCase(files);
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

        differences.AddRange(Stale(directory, files, previous).Select(name => $"{name} is no longer generated"));
        return differences;
    }

    public static void Write(string directory, IReadOnlyList<GeneratedFile> files, IReadOnlyList<string> previous, TextWriter output)
    {
        RefuseNamesThatDifferInCase(files);
        try
        {
            RefuseFilesOfAnotherNamespace(directory, files, previous);
            Directory.CreateDirectory(directory);
            var written = 0;
            foreach (var name in Stale(directory, files, previous))
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

    // The files of an earlier run that this one no longer generates, leaving out those of another namespace.
    private static List<string> Stale(string directory, IReadOnlyList<GeneratedFile> files, IReadOnlyList<string> previous)
    {
        var current = NamespaceOf(files);
        return
        [
            .. previous
                .Except(files.Select(file => file.Name), StringComparer.Ordinal)
                .Where(name => current is null || NamespaceOf(File.ReadLines(Path.Combine(directory, name), Utf8)) is not { } other || other == current)
                .Order(StringComparer.Ordinal),
        ];
    }

    // Two names that differ in case alone are one file where the file system ignores case, so one would overwrite the other
    // and --check would never pass.
    private static void RefuseNamesThatDifferInCase(IReadOnlyList<GeneratedFile> files)
    {
        if (files.GroupBy(file => file.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1) is { } clash)
        {
            throw new CliException(
                CliErrorCodes.FileNamesClash,
                $"The files {string.Join(" and ", clash.Select(file => file.Name).Order(StringComparer.Ordinal))} differ in case alone, so a file system that ignores case keeps one of them; rename one of the types they are generated from. Nothing was written.");
        }
    }

    private static void RefuseFilesOfAnotherNamespace(string directory, IReadOnlyList<GeneratedFile> files, IReadOnlyList<string> previous)
    {
        if (NamespaceOf(files) is not { } current)
        {
            return;
        }

        var generated = files.Select(file => file.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in previous.Where(generated.Contains).Order(StringComparer.Ordinal))
        {
            if (NamespaceOf(File.ReadLines(Path.Combine(directory, name), Utf8)) is { } other && other != current)
            {
                throw new CliException(
                    CliErrorCodes.FileOfAnotherNamespace,
                    $"{name} in {directory} was generated into the namespace {other}, not {current}. Generate each namespace into a folder of its own, or remove the files of {other} first. Nothing was written.");
            }
        }
    }

    // The namespace the C# files of a run declare, or null for files that declare none, as TypeScript's.
    private static string? NamespaceOf(IReadOnlyList<GeneratedFile> files) =>
        files.Select(file => NamespaceOf(file.Text.Split('\n'))).FirstOrDefault(name => name is not null);

    private static string? NamespaceOf(IEnumerable<string> lines) =>
        lines.FirstOrDefault(line => line.StartsWith(NamespaceKeyword, StringComparison.Ordinal))?[NamespaceKeyword.Length..].TrimEnd().TrimEnd(';', '{').TrimEnd();

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
