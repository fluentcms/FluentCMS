namespace FluentCMS.Admin.Api;

/// <summary>
/// A <c>multipart/form-data</c> body of an <see cref="ApiRequest"/>, such as the files of an upload. Its files are held in
/// memory, so a request sent again after a 429 sends them again.
/// </summary>
public sealed class ApiForm
{
    /// <summary>The text fields of the form, by name.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The files of the form, in their order.</summary>
    public IReadOnlyList<ApiFormFile> Files { get; init; } = [];
}

/// <summary>A file of an <see cref="ApiForm"/>.</summary>
public sealed class ApiFormFile
{
    /// <summary>Creates a file.</summary>
    /// <param name="name">The name of the form field.</param>
    /// <param name="fileName">The name of the file, without a directory.</param>
    /// <param name="content">The bytes of the file.</param>
    public ApiFormFile(string name, string fileName, ReadOnlyMemory<byte> content)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        Name = name;
        FileName = fileName;
        Content = content;
    }

    /// <summary>The name of the form field.</summary>
    public string Name { get; }

    /// <summary>The name of the file.</summary>
    public string FileName { get; }

    /// <summary>The bytes of the file.</summary>
    public ReadOnlyMemory<byte> Content { get; }
}
