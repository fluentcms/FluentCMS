# Contributing to FluentCMS

Thank you for your interest. FluentCMS is starting over, so there is little to build on yet. See the [announcement](https://github.com/fluentcms/FluentCMS/issues/2675) for the current state.

## Where things are

| Ref | Role |
| --- | --- |
| `main` | The default branch. Pull requests into it must pass CI. |
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

### What CI runs

The `CI` workflow builds the whole solution on Linux and runs every test project there in three shards (units, integration, plugins and host), plus the data jobs, Markdown lint and packaging. The data jobs run the data tests on SQLite, PostgreSQL 15, SQL Server 2019 and SQL Server 2025. This runs for every pull request into `main` and every push to it. On Windows the same runs build the whole solution and run only the SDK unit, core unit and architecture tests. The full Windows suite runs nightly and on manual runs, so a Windows-only failure shows up in the nightly run rather than on a pull request. A failed nightly run opens an issue titled `Nightly CI failed`, or adds a comment to it while it is open.

### Feature branches and validation

For a batch of related issues, create a branch such as `feature/media` from `main`. Each issue branches from it and opens its pull request back into it. The `Feature CI` workflow builds the whole solution on Linux and Windows and runs SDK unit tests, core unit tests, architecture tests and Markdown lint. Integration, data, host and plugin tests, and packaging, run in full CI.

Merge the issue pull requests into the feature branch as they pass review and fast checks. When the batch is ready, open one pull request from the feature branch into `main`. The `CI` workflow automatically runs all test projects on Linux, the fast tests on Windows, the data jobs, Markdown lint and packaging. Fix any failures on the feature branch and push again; the full checks rerun. Keep the feature branch current with `main` and rerun validation after incorporating changes from it. A passing run from before a change does not validate the updated code.

The fast checks have different names from the full checks required by `main`. Keep `main` protected by `Markdown lint`, `Build and check (ubuntu-latest)`, `Tests (units)`, `Tests (integration)`, `Tests (plugins)`, `Build and test (windows-latest)`, `Data tests (sqlite)` and `Pack`. A repository ruleset on `feature/**` requires `Feature markdown lint`, `Fast checks (ubuntu-latest)` and `Fast checks (windows-latest)` instead, so auto-merge works on pull requests into feature branches too.

To check the combined work before opening the final pull request, go to **Actions → CI → Run workflow**, choose the feature branch and select **Run workflow**. This runs the full suite on that branch, including every test project on Windows, whenever needed. The workflow must first be present on `main` for GitHub to show the manual trigger, and the selected branch must contain the updated workflow. Manual runs provide early feedback; the pull request into `main` still runs its own validation, including the proposed merge with `main`, but not the full Windows suite.

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

Workflows pin every action to a full commit SHA, with its version tag in a trailing comment. Dependabot proposes weekly updates for actions and NuGet packages, grouping minor and patch updates per ecosystem. CI fails when a direct or transitive package has a known vulnerability, and deprecated packages only raise a warning.

## Pull requests

The description contains, in this order:

- `Closes #<issue-number>`.
- A short paragraph on what changes and why.
- A "Dependencies" paragraph naming every new package or GitHub Action with its version, or saying there are none.
- The acceptance list from the issue as a checklist. Tick only what you verified.

### Review and merge

- Every pull request is reviewed before it merges. One approval is not required, but a review always happens.
- Review your own diff before opening the pull request and include the fixes in it, so CI validates the reviewed code once instead of again after a follow-up push.
- Every required check must pass. Once the review is done, enable auto-merge with `gh pr merge <number> --auto --squash`; GitHub merges the pull request as soon as every required check passes.
- Branch protection does not require an issue branch to be up to date with its target. Merge the target into the issue branch only to resolve a conflict.
- Pull requests are squash-merged into a linear history. The title becomes the commit title, so it is short and imperative, for example "Update the contributing guide and add decision records".

## Local development

You need the .NET 10 SDK, at the version in `global.json`. The tests use SQLite by default, so no database server is needed and Docker is not required.

To run the data tests on another provider, set `FLUENTCMS_TEST_PROVIDER` to `postgresql`, `sqlserver-2019` or `sqlserver-2025`. The tests then start that server once in a container through Testcontainers and give each test a database of its own on it, so they need Docker with Linux containers. Tests of SQLite itself, such as its journal modes, file locking, conventions and query plans, are skipped on the other providers with the reason. Run one provider at a time:

```bash
FLUENTCMS_TEST_PROVIDER=postgresql dotnet test --project tests/FluentCMS.Data.Tests -c Release
```

The S3 and Azure Blob media storage tests run MinIO and Azurite through Testcontainers and need Docker with Linux containers; without it they are skipped, and the test output says why.

To run a host on PostgreSQL, or media on S3, `compose.yaml` starts PostgreSQL 15 and MinIO at the versions the tests use, on `localhost` only, with their data in volumes. It is for development, not production. The passwords default to `fluentcms`; set `FLUENTCMS_POSTGRES_PASSWORD` and `FLUENTCMS_MINIO_PASSWORD` to change them. PostgreSQL takes its password when it creates the volume, so set it before the first start or delete the volume.

```bash
docker compose up -d
```

The reference host then runs on PostgreSQL with `FluentCms__Database__Provider=PostgreSql`, `ConnectionStrings__FluentCms=Host=127.0.0.1;Database=fluentcms;Username=fluentcms;Password=fluentcms` and `FluentCms__Database__MigrateOnStartup=true`. The reference host keeps media on the local disk; a host of your own that calls `media.UseS3` can use MinIO, which answers S3 on `http://127.0.0.1:9000` (`ForcePathStyle` true) and serves its console on `http://127.0.0.1:9001`, where you create the bucket. `docker compose down` stops both and keeps the data; add `-v` to delete it.

During implementation, run the test project or test class affected by the change. Before opening an issue pull request, into a feature branch or `main`, run the fast checks once:

```bash
dotnet build -c Release -warnaserror
dotnet test --project tests/FluentCMS.Sdk.Tests -c Release --no-build
dotnet test --project tests/FluentCMS.Core.Tests -c Release --no-build
dotnet test --project tests/FluentCMS.Architecture.Tests -c Release --no-build
npx --yes markdownlint-cli2 "**/*.md"
```

CI runs the full suite, so there is no need to run it locally for an issue pull request. Run the full checks to reproduce a CI failure, or before opening the pull request from a feature branch into `main`:

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
