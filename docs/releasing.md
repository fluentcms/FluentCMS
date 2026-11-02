# Releasing

`FluentCMS.Sdk` is the public contract that plugins and host code compile against, and it follows semantic versioning: breaking changes ship only in a major version, and every removal is announced with `[Obsolete]` at least one minor version earlier. The build enforces this with two checks, described below.

## How the build enforces the contract

- **Public API analyzer.** `Microsoft.CodeAnalysis.PublicApiAnalyzers` compares the public surface of `FluentCMS.Sdk` with `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` in `src/FluentCMS.Sdk`. A public member that is in neither file fails the build with `RS0016`; a listed member that no longer exists fails with `RS0017`.
- **Package validation.** `EnablePackageValidation` compares the packed assembly with the last released package and fails `dotnet pack` on a removed or changed member (`CP0001`, `CP0002` and related codes).
- **XML docs.** Every public member needs an XML doc comment, because a missing one is `CS1591` and warnings are errors.

During development, add new public members to `PublicAPI.Unshipped.txt`. The `RS0016` code fix in the IDE does this for you. A removal or signature change is recorded in `PublicAPI.Unshipped.txt` as a `*REMOVED*` line followed by the new entry, and is allowed only while preparing a major version.

## Cutting a release

1. Check that `main` builds: `dotnet build -c Release -warnaserror`.
2. In `src/FluentCMS.Sdk`, move every entry from `PublicAPI.Unshipped.txt` to `PublicAPI.Shipped.txt`. Keep the `#nullable enable` first line in both files, keep `PublicAPI.Shipped.txt` sorted, and leave `PublicAPI.Unshipped.txt` with only the `#nullable enable` line. For a `*REMOVED*` line, delete the entry it names from `PublicAPI.Shipped.txt` and drop the `*REMOVED*` line itself.
3. Commit the two files with the release, for example `Ship public API for 1.1.0`.
4. Tag and publish the packages.
5. Open a follow-up pull request that sets `PackageValidationBaselineVersion` in `src/FluentCMS.Sdk/FluentCMS.Sdk.csproj` to the version that was just released, so the next release is compared with it. Until 1.0.0 has shipped there is no baseline and the property is absent; the first release adds it.

## Breaking changes

- A breaking change to a shipped member lands only in a major version.
- Announce the removal one minor version ahead: mark the member `[Obsolete("Use X instead.")]` in a minor release, then remove it in the next major. The `FluentCMS.Analyzers` package reports obsolete SDK members to plugin authors.
- For a major version, set `PackageValidationBaselineVersion` to the last release of the previous major, and record each intentional break by running `dotnet pack -c Release -p:ApiCompatGenerateSuppressionFile=true`. Review the generated `CompatibilitySuppressions.xml` in the pull request, and delete it once the major has shipped and the baseline has moved to it.
