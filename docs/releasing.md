# Releasing

`FluentCMS.Sdk` is the public contract that plugins and host code compile against, and it follows semantic versioning: breaking changes ship only in a major version, and every removal is announced with `[Obsolete]` at least one minor version earlier. The build enforces this with two checks, described below.

## How the build enforces the contract

- **Public API analyzer.** `Microsoft.CodeAnalysis.PublicApiAnalyzers` compares the public surface of `FluentCMS.Sdk` with `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` in `src/FluentCMS.Sdk`. A public member that is in neither file fails the build with `RS0016`; a listed member that no longer exists fails with `RS0017`.
- **Package validation.** `EnablePackageValidation` compares the packed assembly with the last released package and fails `dotnet pack` on a removed or changed member (`CP0001`, `CP0002` and related codes).
- **XML docs.** Every public member needs an XML doc comment, because a missing one is `CS1591` and warnings are errors.

During development, add new public members to `PublicAPI.Unshipped.txt`. The `RS0016` code fix in the IDE does this for you. A removal or signature change is recorded in `PublicAPI.Unshipped.txt` as a `*REMOVED*` line followed by the new entry, and is allowed only while preparing a major version.

## Cutting a release

1. Check that `main` builds: `dotnet build -c Release -warnaserror`.
2. Check that the `Fresh machine` jobs of CI passed on the commit to release, on Linux and Windows, or run them with **Actions → Fresh machine → Run workflow**. They run [`eng/fresh-machine.ps1`](../eng/fresh-machine.ps1), which packs every package into a folder feed and, on a clean runner, installs the templates and the `fluentcms` tool from it, makes a Blog host with `dotnet new fluentcms`, runs `migrations add` and `migrate` on SQLite (and on PostgreSQL on Linux), makes an administrator and a key, compiles the output of `generate typescript`, exports the site and imports it into a second new host, and builds and tests a plugin from `dotnet new fluentcms-plugin`. The Linux job then checks that a package that misses a file and a template that names a wrong version fail the script. The script runs on a developer's machine too (PowerShell 7.4, the .NET SDK and Node.js); beyond the repository's own build output, it leaves nothing behind but its work directory.
3. In `src/FluentCMS.Sdk`, move every entry from `PublicAPI.Unshipped.txt` to `PublicAPI.Shipped.txt`. Keep the `#nullable enable` first line in both files, keep `PublicAPI.Shipped.txt` sorted, and leave `PublicAPI.Unshipped.txt` with only the `#nullable enable` line. For a `*REMOVED*` line, delete the entry it names from `PublicAPI.Shipped.txt` and drop the `*REMOVED*` line itself.
4. Commit the two files with the release, for example `Ship public API for 1.1.0`.
5. Tag and publish the packages.
6. Open a follow-up pull request that sets `PackageValidationBaselineVersion` in `src/FluentCMS.Sdk/FluentCMS.Sdk.csproj` to the version that was just released, so the next release is compared with it. Until 1.0.0 has shipped there is no baseline and the property is absent; the first release adds it.

## Breaking changes

- A breaking change to a shipped member lands only in a major version.
- Announce the removal one minor version ahead: mark the member `[Obsolete("Use X instead.")]` in a minor release, then remove it in the next major. The compiler warns a plugin author who uses an obsolete member (`CS0618`). A `FluentCMS.Analyzers` rule for obsolete SDK members follows with the first member that is marked `[Obsolete]`; until then the package holds the schema rules only ([analyzer rules](analyzers.md)).
- For a major version, set `PackageValidationBaselineVersion` to the last release of the previous major, and record each intentional break by running `dotnet pack -c Release -p:ApiCompatGenerateSuppressionFile=true`. Review the generated `CompatibilitySuppressions.xml` in the pull request, and delete it once the major has shipped and the baseline has moved to it.
