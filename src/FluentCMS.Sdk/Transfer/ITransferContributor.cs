namespace FluentCMS.Transfer;

/// <summary>
/// Puts the data a plugin owns into a transfer package and takes it back out. Core runs a contributor on the export and on
/// the import of a site in which its plugin is enabled, under <c>plugins/{Name}/</c> of the package, and imports it before
/// the content of the package, so that an entry can refer to what the contributor brought. Register it with
/// <c>IPluginServiceBuilder.AddTransferContributor</c>.
/// </summary>
public interface ITransferContributor
{
    /// <summary>
    /// The folder of the contributor in a package: lower case letters, digits and hyphens, unique among the contributors of
    /// the host. It must not change between releases, or an older package is no longer read.
    /// </summary>
    string Name { get; }

    /// <summary>Writes the files of the contributor for the site of the export.</summary>
    /// <param name="context">The site, the options and the writer of the package.</param>
    /// <param name="cancellationToken">Cancels the export, as when the caller goes away.</param>
    /// <returns>A task that completes when every file is written.</returns>
    Task ExportAsync(ITransferExport context, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the files of the contributor into the site of the import, and reports each item. In a dry run it checks
    /// everything, writes nothing, and reports what it would have done. An item that cannot be imported is reported as an
    /// error and the others go on; an exception stops the import of the whole package.
    /// </summary>
    /// <param name="context">The site, the options, the reader of the package and the report.</param>
    /// <param name="cancellationToken">Cancels the import, as when the caller goes away.</param>
    /// <returns>A task that completes when every file is read.</returns>
    Task ImportAsync(ITransferImport context, CancellationToken cancellationToken);
}
