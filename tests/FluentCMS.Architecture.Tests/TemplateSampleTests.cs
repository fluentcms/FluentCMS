using Shouldly;
using Xunit;

namespace FluentCMS.Architecture.Tests;

// Spec 6.2: `--sample blog` gives a copy of the Blog sample in the project, since the sample is not a package. The copy
// is the sample with the template's namespace, but for the files named here.
public class TemplateSampleTests
{
    private const string Sample = "samples/Blog";
    private const string Copy = "templates/fluentcms/Blog";

    // The sample's project, and its OpenAPI examples, which the template leaves out.
    private static readonly string[] OnlyInTheSample = ["Blog.csproj", "BlogExamples.cs"];

    // Registers the content types from the project's assembly, and no OpenAPI examples.
    private static readonly string[] Different = ["BlogSample.cs"];

    [Fact]
    public void TheTemplatesBlogIsTheSampleWithTheTemplatesNamespace()
    {
        var sample = Files(Sample);
        var copy = Files(Copy);

        copy.ShouldBe(sample.Except(OnlyInTheSample, StringComparer.Ordinal), ignoreOrder: true);
        foreach (var file in copy.Except(Different, StringComparer.Ordinal))
        {
            var expected = Read(Sample, file).Replace("FluentCMS.Samples.Blog", "FluentCms_App.Blog", StringComparison.Ordinal);
            Read(Copy, file).ShouldBe(expected, $"{Copy}/{file} differs from {Sample}/{file} beyond the namespace; change both, or name it in Different.");
        }
    }

    private static List<string> Files(string folder)
    {
        var root = Path.Combine(ProjectFile.FindRepositoryRoot(), folder);
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        return [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .Where(path => !path.Split(separators).Any(part => part is "bin" or "obj"))
            .Select(path => path.Replace('\\', '/'))];
    }

    // Line endings depend on the checkout.
    private static string Read(string folder, string file) =>
        File.ReadAllText(Path.Combine(ProjectFile.FindRepositoryRoot(), folder, file)).ReplaceLineEndings("\n");
}
