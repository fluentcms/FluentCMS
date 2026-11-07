# Contributing to FluentCMS

Thank you for your interest. FluentCMS is starting over, so there is little to build on yet. See the [announcement](https://github.com/fluentcms/FluentCMS/issues/2675) for the current state.

## Where things are

| Ref | Role |
| --- | --- |
| `main` | The default branch. Pull requests into it must pass the full CI suite. |
| `feature/*` | Integration branches for a group of related issues. Issue pull requests into them run fast checks. |
| `archive/*` | Earlier code, kept for reference. Read-only and not maintained. |

| Path | Role |
| --- | --- |
| `docs/planning/` | The plan: the [backend technical specification](docs/planning/backend-technical-spec.md) and the [specification](docs/planning/specification/README.md) it builds on, including the [decisions log](docs/planning/specification/12-decisions-log.md). |
| `docs/decisions/` | Decision records for choices made during implementation. See its [README](docs/decisions/README.md). |

## Workflow: issue first

1. **Every change starts from an issue.** For anything beyond a small fix, wait for a maintainer to confirm the approach before writing code.
2. **Branch from the intended target**, named `<issue-number>-<short-slug>`, for example `2680-contributing-guide`. Use the agreed `feature/*` branch when the issue belongs to a batch of related work; otherwise use `main`.
3. **Open a pull request against that target.** Put `Closes #<issue-number>` in the description, and keep an issue pull request to its issue.
4. **CI must pass.** Pull requests are squash-merged, so the pull request title becomes the commit title. Keep it short and imperative.

### Feature branches and validation

For a batch of related issues, create a branch such as `feature/media` from `main`. Each issue branches from it and opens its pull request back into it. The `Feature CI` workflow builds the whole solution on Linux and Windows and runs SDK unit tests, core unit tests, architecture tests and Markdown lint. Integration, data, host and plugin tests, and packaging, run in full CI.

Merge the issue pull requests into the feature branch as they pass review and fast checks. When the batch is ready, open one pull request from the feature branch into `main`. The `CI` workflow automatically runs all test projects on Linux and Windows, the SQLite data job, Markdown lint and packaging. Fix any failures on the feature branch and push again; the full checks rerun. Keep the feature branch current with `main` and rerun validation after incorporating changes from it. A passing run from before a change does not validate the updated code.

The fast checks have different names from the full checks required by `main`. Keep `main` protected by `Markdown lint`, `Build and test (ubuntu-latest)`, `Build and test (windows-latest)`, `Data tests (sqlite)` and `Pack`. For protected feature branches, require `Feature markdown lint`, `Fast checks (ubuntu-latest)` and `Fast checks (windows-latest)` instead.

To check the combined work before opening the final pull request, go to **Actions → CI → Run workflow**, choose the feature branch and select **Run workflow**. This runs the full suite on that branch whenever needed. The workflow must first be present on `main` for GitHub to show the manual trigger, and the selected branch must contain the updated workflow. Manual runs provide early feedback; the pull request into `main` still runs its own full validation, including the proposed merge with `main`.

List the included issues with `Closes #<issue-number>` in the final pull request into `main`, so GitHub closes them when the batch reaches the default branch. A pull request targeting a feature branch does not close them on merge.

## Labels

Every issue has one `type:*` label. Issues that belong to a part of the codebase also have one `area:*` label.

| Label | Meaning |
| --- | --- |
| `type:feature` | A new capability. |
| `type:bug` | A defect. |
| `type:chore` | Repository, tooling or housekeeping. |
| `type:docs` | Documentation. |
| `type:epic` | A milestone-level feature, sliced into issues. |
| `type:adr` | A proposal for an architecture decision. |
| `type:question` | A question or a request for clarification. |

`area:*` labels name the part of the codebase: `build`, `sdk`, `schema`, `storage`, `content`, `publishing`, `localization`, `identity`, `authorization`, `jobs`, `events`, `settings` and `testing`.

## Milestones and tracking issues

Work is planned in milestones, `M0 Foundation` first. The goal and exit criteria of each are in section 8 of the [backend technical specification](docs/planning/backend-technical-spec.md). Each milestone has one tracking issue holding a checklist of its issues, for example [#2707](https://github.com/fluentcms/FluentCMS/issues/2707) for M0 and [#2708](https://github.com/fluentcms/FluentCMS/issues/2708) for M1. Issues are created milestone by milestone, so later milestones appear when they start. Tick the tracking issue when your pull request merges.

## Dependencies

A new NuGet package or GitHub Action is named in its issue, with the version, before it is added, so that it is visible in review. Package versions live in `Directory.Packages.props`. The pull request that adds the dependency repeats it in a "Dependencies" paragraph.

## Pull requests

The description contains, in this order:

- `Closes #<issue-number>`.
- A short paragraph on what changes and why.
- A "Dependencies" paragraph naming every new package or GitHub Action with its version, or saying there are none.
- The acceptance list from the issue as a checklist. Tick only what you verified.

### Review and merge

- Every pull request is reviewed before it merges. One approval is not required, but a review always happens.
- Every required check must pass.
- Pull requests are squash-merged into a linear history. The title becomes the commit title, so it is short and imperative, for example "Update the contributing guide and add decision records".

## Local development

You need the .NET 10 SDK, at the version in `global.json`. SQLite is the only database used during implementation, so no database server is needed and Docker is not required.

During implementation, run the test project or test class affected by the change. Before opening an issue pull request into a feature branch, run the fast checks:

```bash
dotnet build -c Release -warnaserror
dotnet test --project tests/FluentCMS.Sdk.Tests -c Release --no-build
dotnet test --project tests/FluentCMS.Core.Tests -c Release --no-build
dotnet test --project tests/FluentCMS.Architecture.Tests -c Release --no-build
npx --yes markdownlint-cli2 "**/*.md"
```

Before opening a pull request into `main`, run the full checks:

```bash
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet pack -c Release --no-build -o artifacts/packages
npx --yes markdownlint-cli2 "**/*.md"
```

## Decisions

The plan lives in `docs/planning/`. When implementation needs a choice that the [decisions log](docs/planning/specification/12-decisions-log.md) and the backend specification do not already settle, record it in `docs/decisions/` as a short decision record. Open an issue labelled `type:adr` to propose it. The [README](docs/decisions/README.md) there has the details and the [template](docs/decisions/0000-template.md).

## Authorship

Commits and pull requests are authored by the person who submits them. Do not add tool-generated co-author trailers or "generated by" footers. You are responsible for what you submit, whatever tools you used to write it.
