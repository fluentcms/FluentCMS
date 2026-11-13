using FluentCMS.Sites;

namespace FluentCMS.Transfer;

/// <summary>An import as a contributor sees it: the site, the options, the package being read and the report.</summary>
public interface ITransferImport
{
    /// <summary>The site that is imported into, which is the site of the request, whatever the site of the package.</summary>
    SiteId Site { get; }

    /// <summary>The site the package was exported from, so that a value that names it can be moved to <see cref="Site"/>.</summary>
    SiteId PackageSite { get; }

    /// <summary>The options of the import that core does not read itself, by name. A contributor reads the ones it documents.</summary>
    IReadOnlyDictionary<string, string> Options { get; }

    /// <summary>Whether the import checks everything and writes nothing.</summary>
    bool DryRun { get; }

    /// <summary>Whether an item that is newer in the instance than in the package is taken from the package all the same.</summary>
    bool Overwrite { get; }

    /// <summary>The files of the contributor's folder in the package, by their paths in it, such as <c>assets.jsonl</c>.</summary>
    IReadOnlyList<string> Paths { get; }

    /// <summary>Reads one file of the contributor's folder. The stream is valid only inside <paramref name="read"/>.</summary>
    /// <param name="path">The path in the contributor's folder, one of <see cref="Paths"/>.</param>
    /// <param name="read">Reads the content of the file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true"/> when the file is in the package and was read; otherwise <see langword="false"/>.</returns>
    Task<bool> ReadAsync(string path, Func<Stream, CancellationToken, Task> read, CancellationToken cancellationToken);

    /// <summary>The same as <c>Report(section, id, outcome, null, null)</c>, for an item that has no code or message.</summary>
    /// <param name="section">The kind of item, such as <c>media</c> for an asset.</param>
    /// <param name="id">The id of the item.</param>
    /// <param name="outcome">What happened to it.</param>
    void Report(string section, string id, TransferOutcome outcome) => Report(section, id, outcome, null, null);

    /// <summary>
    /// Reports what happened to one item. The report counts the outcomes by section and names the items that were not
    /// created, updated or skipped. An item reported as created, updated or skipped under the name of a reference target,
    /// such as <c>media</c>, counts as there for the entries of a dry run that refer to it.
    /// </summary>
    /// <param name="section">The kind of item, such as <c>media</c> for an asset.</param>
    /// <param name="id">The id of the item.</param>
    /// <param name="outcome">What happened to it.</param>
    /// <param name="code">The code of a conflict or an error, or <see langword="null"/>.</param>
    /// <param name="message">What went wrong, for people, or <see langword="null"/>.</param>
    void Report(string section, string id, TransferOutcome outcome, string? code, string? message);
}
