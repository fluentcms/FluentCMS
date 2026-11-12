using FluentCMS.Sites;

namespace FluentCMS.Transfer;

/// <summary>An export as a contributor sees it: the site, the options and the package being written.</summary>
public interface ITransferExport
{
    /// <summary>The site that is exported.</summary>
    SiteId Site { get; }

    /// <summary>
    /// The options of the export that core does not read itself, by name, such as <c>mediaFiles</c>. A contributor reads the
    /// ones it documents and ignores the rest.
    /// </summary>
    IReadOnlyDictionary<string, string> Options { get; }

    /// <summary>Writes one file of the contributor's folder. Files are written one at a time, and the stream is valid only inside <paramref name="write"/>.</summary>
    /// <param name="path">The path in the contributor's folder, with forward slashes, such as <c>assets.jsonl</c>.</param>
    /// <param name="write">Writes the content of the file.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="ArgumentException">The path is empty, absolute, or leaves the folder.</exception>
    Task WriteAsync(string path, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken);
}
