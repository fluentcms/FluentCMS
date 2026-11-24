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

The `CI` workflow builds the whole solution on Linux and runs every test project there in six shards (units with the tool and the client, two of integration tests, two of plugins and host, admin), plus the data jobs, Markdown lint, packaging and the templates job, which installs the `dotnet new` templates from the packed packages, creates and builds every combination, runs the plugin's tests and starts the SQLite hosts and a PostgreSQL host (`templates/test-templates.sh`). This runs for every pull request into `main` and every push to it. The data jobs run the data tests on SQLite and PostgreSQL 15 in every run. SQL Server 2025 joins them on pushes to `main`, on pull requests from `feature/*` branches, on pull requests that change a data provider (`src/FluentCMS.Data.*`), a migration or the data tests, and on the nightly and manual runs, and SQL Server 2019 only on the nightly and on manual runs that are not quick. An issue pull request therefore needs no manual run for SQL Server. Each server runs in two shards, split by namespace. On Windows the same runs build the whole solution and run only the fast tests: the SDK unit, core unit and architecture tests, the admin's component tests (`FluentCMS.Admin.Tests`), the tool's tests (`FluentCMS.Cli.Tests`) and the host's commands with export and import on SQLite (the `Commands` namespace of `FluentCMS.Core.IntegrationTests`). The full Windows suite runs nightly and on manual runs that are not quick, so a Windows-only failure shows up in the nightly run rather than on a pull request. A failed or cancelled nightly run opens an issue titled `Nightly CI failed`, or adds a comment to it while it is open. A newer run of a pull request cancels its older one; a push to `main`, the nightly run and a manual run each run in a group of their own and are never cancelled by another run.

The shards are not listed by hand: the `Plan` job runs `eng/test-shards.sh`, which gives each test project of `FluentCMS.slnx` (each project that references `xunit.v3`) the shard of the first rule its path matches, so a new test project under `tests/` or `admin/tests/` runs without a change to the workflow. `Plan` fails when a test project matches no rule, when a shard is left empty, and when a `package-lock.json` sits in a folder that no job of `ci.yml` runs in. A test project that discovers no tests fails its run (exit code 8), and a filtered run also sets a minimum with `--minimum-expected-tests`.

The admin's tests are under `admin/tests/`. The component tests (`FluentCMS.Admin.Tests`) run on bUnit 2.15.2 with xUnit v3 and Shouldly and need no browser, so they join the fast checks. The `Tests (admin)` shard runs them with the API tests (`FluentCMS.Admin.ApiTests`), which run the admin's client against `FluentCmsTestHost` and run in full CI only. The end-to-end tests (`FluentCMS.Admin.EndToEnd`) run in full CI only, in the `End-to-end (admin)` job: they publish the reference host, run it in Production on SQLite with the Blog sample on a free port of `127.0.0.1` over https with a certificate the run makes, seed it with its own commands, and drive the admin at `/admin` in Chromium with Playwright 1.64.1. The job downloads Chromium's headless shell on every run, which takes seconds, and fails a skipped test, so a browser that did not install fails the job. A case that fails is run once more on a new host from the same publish (`run-with-retry.ps1`): one that passes then is named as flaky in the job summary and the job passes, and one that fails twice fails it. A skipped case, more than five failed cases, or a run that ended otherwise, such as by its timeout, fails the job without a second run. A manual run of `CI`, or of `Compose` for the run on the image, with the `traces` input keeps a Playwright trace of each failed test in the job's test results, with the network log and response bodies left out and the passwords, tokens, codes and cookies the browser saw masked. A failed case also prints the address of each page its browser had open and the last lines of the host's log, masked the same way. The `Pack` job publishes `FluentCMS.Admin.App` trimmed, and a trim warning in a FluentCMS assembly fails it; it makes the prebuilt static build of the admin from that publish, `fluentcms-admin-{version}.zip`, which `eng/admin-zip.cs` checks ([hosting the admin](docs/admin-hosting.md)).

The `Compose` workflow builds the image, starts `src/FluentCMS.Host/compose.yaml` on PostgreSQL and MinIO and runs a smoke test against it ([Docker](docs/docker.md#compose-with-postgresql-and-minio)). It runs on pushes to `feature/*` branches, `CI` calls it on pushes to `main` and on manual runs that are not quick, and `Publish` calls it on the image it built; pull requests run it only through the `Publish` dry run. Beside the smoke test, its `End-to-end on the image (admin)` job builds the image once and runs the end-to-end project against a container of it (admin spec 7), leaving out the cases with the trait `Host=Publish`, which need what only a publish of the repository has, such as the pseudo-cultures or the static build of the admin. The run holds the host, its commands as one-off containers on a volume of the run, and Chromium against its https port on `127.0.0.1`. A failed case is run once more against the same image, a flaky one is named in the summary as in `End-to-end (admin)`, and a skipped case fails the job. A manual run of `Compose` with an image runs that published image without building one. The job is left out when `Publish` calls the workflow, so a release waits for the smoke test only.

The `Fresh machine` workflow runs `eng/fresh-machine.ps1`, the path of a new user from the packages alone: it packs every package into a folder feed and, in a work directory outside the repository, installs the templates and the `fluentcms` tool from it, makes a Blog host, adds and applies its migrations, makes an administrator and a key, compiles the generated TypeScript, exports the site and imports it into a second host, and builds and tests a plugin. Linux runs it on SQLite and PostgreSQL and then checks that a broken feed fails it, and Windows runs it on SQLite. It runs on pushes to `feature/*` branches, and `CI` calls it on pushes to `main`, on manual runs and on the pull request of a feature branch into `main`; issue pull requests do not run it. Run it locally with `pwsh eng/fresh-machine.ps1`, which needs PowerShell 7.4, the .NET SDK and Node.js, takes PostgreSQL with `-PostgreSql` and `-PostgreSqlImport` (two empty databases), and keeps its work directory for the logs. `-Feed` with `-Version` runs it against packages that are published already, such as nuget.org after a release, and so does a manual run of the workflow with a feed and a version.

The `Publish` workflow publishes a release from a tag `v*` on `main`: the packages to nuget.org, the image to `ghcr.io/fluentcms/fluentcms` and the GitHub release ([releasing](docs/releasing.md)). A pull request that changes the packaging, such as a project file under `src/` or `admin/src/`, `Directory.Build.props` or the Dockerfile, runs it as a dry run, which packs and builds everything, runs the `Compose` smoke test on the image it built, lists what a release would push and pushes nothing. Every package carries one version, from `VersionPrefix` in `Directory.Build.props`: builds that are not releases get a pre-release suffix, `ci.{run id}.{attempt}` in CI and `dev` locally. A new packable project needs a `README.md` beside it and a `Description`, which the `Pack` job checks. `eng/check-packages.cs` expects SourceLink to point at the `origin` of the clone, so on a fork it checks the fork's own repository.

### Feature branches and validation

For a batch of related issues, create a branch such as `feature/media` from `main`. Each issue branches from it and opens its pull request back into it. The `Feature CI` workflow builds the whole solution on Linux and Windows and runs the fast tests, the same as the Windows job of `CI`: SDK unit tests, core unit tests, architecture tests, the admin's component tests, the tool's tests and the host's commands on SQLite, and Markdown lint. The rest of the integration tests, the data, host and plugin tests, packaging and the templates job run in full CI.

Merge the issue pull requests into the feature branch as they pass review and fast checks; enable auto-merge on each as soon as its review is done, so that nobody waits on a check. Keep the heavy steps out of the fast checks: a trimmed publish, a browser test or a job that needs a container belongs in full CI. When the batch is ready, open one pull request from the feature branch into `main`. The `CI` workflow automatically runs all test projects on Linux, the fast tests on Windows, the data jobs, Markdown lint, packaging and the templates job. Fix any failures on the feature branch and push again; the full checks rerun. Keep the feature branch current with `main` and rerun validation after incorporating changes from it. A passing run from before a change does not validate the updated code.

The fast checks have different names from the full checks required by `main`. Keep `main` protected by `Markdown lint`, `Build and check (ubuntu-latest)`, `Tests (units)`, `Tests (integration)`, `Tests (plugins)`, `Build and test (windows-latest)`, `Data tests (sqlite)`, `Data tests (postgresql)` and `Pack`. `Tests (integration)`, `Tests (plugins)` and `Data tests (postgresql)` are no longer test jobs: the `required` job reports each name and passes only when every shard of the tests and of the data tests passed, so the protection needs no change when a shard is added. `Tests (admin)` becomes required when the admin's milestone A0 reaches `main`, and `End-to-end (admin)` when A1 does: until then a pull request into `main` has no such check, so requiring it would block every one. A repository ruleset on `feature/**` requires `Feature markdown lint`, `Fast checks (ubuntu-latest)` and `Fast checks (windows-latest)` instead, so auto-merge works on pull requests into feature branches too.

To check the combined work before opening the final pull request, go to **Actions → CI → Run workflow**, choose the feature branch and select **Run workflow**. This runs the full suite on that branch, including every test project on Windows, whenever needed. Run it in the middle of a milestone as well, not only at the end, so that a failure in a job the fast checks skip is found while the work that caused it is still open. For that check, tick **quick** (`gh workflow run ci.yml --ref <branch> -f quick=true`): the run then is the one the pull request into `main` gets, with the fast tests on Windows, SQL Server 2025 without 2019 and no `Compose`, and takes less than half the time of the full run, whose Windows job alone takes more than 20 minutes. Keep the full run for the end of the milestone. The workflow must first be present on `main` for GitHub to show the manual trigger, and the selected branch must contain the updated workflow. Manual runs provide early feedback; the pull request into `main` still runs its own validation, including the proposed merge with `main`, but not the full Windows suite.

List the included issues with `Closes #<issue-number>` in the final pull request into `main`, so GitHub closes them when the batch reaches the default branch. A pull request targeting a feature branch does not close them on merge.

## Labels

Every issue has one `type:*` label. Issues that belong to a part of the codebase also have one `area:*` label.

| Label | Meaning |
| --- | --- |
| `type:feature` | A new capability. |
| `type:bug` | A defect. A review finding is one only when it is a blocker: see [Review findings](#review-findings). |
| `type:chore` | Repository, tooling or housekeeping. |
| `type:docs` | Documentation. |
| `type:epic` | A milestone-level feature, sliced into issues. |
| `type:adr` | A proposal for an architecture decision. |
| `type:question` | A question or a request for clarification. |

`area:*` labels name the part of the codebase: `build`, `sdk`, `schema`, `storage`, `content`, `publishing`, `localization`, `identity`, `authorization`, `jobs`, `events`, `settings`, `testing` and `admin`.

## Milestones and tracking issues

Work is planned in milestones, `M0 Foundation` first. The goal and exit criteria of the backend's milestones, M0 to M7, are in section 8 of the [backend technical specification](docs/planning/backend-technical-spec.md); those of the admin's, A0 onward, in section 9 of the [admin technical specification](docs/planning/admin-technical-spec.md), whose issues are numbered `A-nn` and labelled `area:admin`. Each milestone has one tracking issue holding a checklist of its issues, for example [#2707](https://github.com/fluentcms/FluentCMS/issues/2707) for M0, [#2708](https://github.com/fluentcms/FluentCMS/issues/2708) for M1 and [#3151](https://github.com/fluentcms/FluentCMS/issues/3151) for A0. Issues are created milestone by milestone, so later milestones appear when they start. Tick the tracking issue when your pull request merges.

## Dependencies

A new NuGet package or GitHub Action is named in its issue, with the version, before it is added, so that it is visible in review. Package versions live in `Directory.Packages.props`. The pull request that adds the dependency repeats it in a "Dependencies" paragraph.

Workflows pin every action to a full commit SHA, with its version tag in a trailing comment. Restore takes every package from nuget.org alone, through the `nuget.config` at the root, which the image build copies too. Dependabot proposes weekly updates for actions, NuGet packages, the npm projects (`clients/typescript` and the `js` folder of an admin project), the base images of the two Dockerfiles and the images of the two compose files, grouping minor and patch updates per ecosystem. Images are pinned by tag and digest, so that Dependabot can move both; it leaves out a new major of .NET and of PostgreSQL. The data and media tests and the workflows' services pin the PostgreSQL and MinIO digests too, which an update of the compose files changes with them. CI fails when a direct or transitive package has a known vulnerability, and deprecated packages only raise a warning.

## Third-party code

Code taken in from another work, whole or in part, gets its notice in `THIRD-PARTY-NOTICES.md` at the root: the source and its commit, where the code is used and the licence kept verbatim, and the file that holds the code says so in a comment. That file travels with every copy of the compiled code, so keep the name, and add `<None Include="../../../THIRD-PARTY-NOTICES.md" Pack="true" PackagePath="" Visible="false" />` to the project of a package that ships the code, as `FluentCMS.Admin` (the QR code encoder) and `FluentCMS.Admin.RichText` (the editor's bundle) do. A package declares MIT by `PackageLicenseExpression` and does not carry `LICENSE`. The image holds `LICENSE` and the notices in `/app`, and the published admin holds both as static files next to the app, copied by `admin/src/FluentCMS.Admin.App/FluentCMS.Admin.App.csproj`; a new place that ships compiled code, such as an archive of the admin, copies both files too.

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

### Review findings

A review sorts each finding as a blocker or a follow-up and names its evidence: a failing test, a reproduction or "from reading". A follow-up filed as a bug of its own brings a pull request, a review of that and a full CI run, and the review brings more follow-ups, so only blockers get that path.

- **A blocker** is a security hole, a loss or corruption of data, a wrong result on an ordinary path, or a finding shown by a failing test or a reproduction. It is fixed in the pull request before it merges. Found after the merge, it becomes a `type:bug` issue.
- **A follow-up** is everything else: a finding from reading alone, a race that needs requests in parallel on one row, a decision the specification still owes, the wording of a message or a guide, test hygiene, or something older than the pull request. It does not block the merge and gets no issue of its own. Add it as a line to the follow-up list of its area: [#3608](https://github.com/fluentcms/FluentCMS/issues/3608) for content, [#3609](https://github.com/fluentcms/FluentCMS/issues/3609) for identity and access, [#3610](https://github.com/fluentcms/FluentCMS/issues/3610) for the admin and [#3611](https://github.com/fluentcms/FluentCMS/issues/3611) for storage, jobs, events and settings. Open a new list, labelled `type:chore` with its `area:*` labels, only for an area none of them covers. One pull request works a list off at the end of a milestone.
- **One review per pull request.** A second follows only a fix round that changed security, deletion or model-change code. There is no third: after two, the maintainer decides.
- **Review before the pull request opens** when the reviewer can read the branch: push the branch, have `origin/<target>...<branch>` reviewed, push the fixes and then open the pull request, so CI runs once.
- **Ask a reviewer for correctness gaps** against the issue's acceptance list and the specification, not for problems in general: a review asked to find problems always finds some.

## Local development

You need the .NET 10 SDK, at the version in `global.json`. The tests use SQLite by default, so no database server is needed and Docker is not required.

To run the data tests on another provider, set `FLUENTCMS_TEST_PROVIDER` to `postgresql`, `sqlserver-2019` or `sqlserver-2025`. The tests then start that server once in a container through Testcontainers and give each test a database of its own on it, so they need Docker with Linux containers. Tests of SQLite itself, such as its journal modes, file locking, conventions and query plans, are skipped on the other providers with the reason. Run one provider at a time:

```bash
FLUENTCMS_TEST_PROVIDER=postgresql dotnet test --project tests/FluentCMS.Data.Tests -c Release
```

The S3 and Azure Blob media storage tests run MinIO and Azurite through Testcontainers and need Docker with Linux containers; without it they are skipped, and the test output says why.

The admin's end-to-end tests (`admin/tests/FluentCMS.Admin.EndToEnd`) need Chromium for Playwright; without it the browser tests are skipped with a message, and the tests of the host they start, which need no browser, still run. They publish the reference host with no build, so build it in the same configuration first, which building the project does. No trust store, certificate store or machine setting is changed: the browser accepts the certificate of the run's own host by its key. On Windows the host imports the run's certificate files with default flags, so their private keys stay in the user's key folder (`%APPDATA%\Microsoft\Crypto\Keys`) until [#3212](https://github.com/fluentcms/FluentCMS/issues/3212). To run them, install the browser once with PowerShell, then set `FLUENTCMS_E2E_TRACES` to a folder to keep a masked trace of each failed test, which `npx playwright show-trace` opens:

```bash
dotnet build admin/tests/FluentCMS.Admin.EndToEnd -c Release
pwsh admin/tests/FluentCMS.Admin.EndToEnd/bin/Release/net10.0/playwright.ps1 install chromium
dotnet test --project admin/tests/FluentCMS.Admin.EndToEnd -c Release --no-build
```

Before pushing a new or changed case, run it once in slow mode, which makes Chromium as slow as a two-core CI runner: `FLUENTCMS_E2E_SLOW=1` throttles the CPU of every page four times through the DevTools protocol (set it to another rate, such as `6`, for more) and adds 150 ms to each request (`FLUENTCMS_E2E_SLOW_LATENCY` in milliseconds). A case that passes on your machine and fails in slow mode waits for the wrong thing: fix its wait, not the throttle. One run in slow mode replaces repeated runs. The host process is not slowed, and a page that a page opens, as `window.open` does, is throttled once it has opened, so its first load may run at full speed. In PowerShell:

```powershell
$env:FLUENTCMS_E2E_SLOW = '1'; dotnet test --project admin/tests/FluentCMS.Admin.EndToEnd -c Release --no-build
```

The cases must pass in any order and one class alone. xUnit.net runs the classes of one collection in a fixed order that `--seed` does not change, so `FLUENTCMS_E2E_SHUFFLE` set to a number shuffles the classes and the cases of each class by it. Run them shuffled, and one class alone by its name:

```powershell
$env:FLUENTCMS_E2E_SHUFFLE = '4242'; dotnet test --project admin/tests/FluentCMS.Admin.EndToEnd -c Release --no-build
```

```bash
dotnet test --project admin/tests/FluentCMS.Admin.EndToEnd -c Release --no-build --filter-class FluentCMS.Admin.EndToEnd.MediaLibraryTests
```

To run them against the image, as the `End-to-end on the image (admin)` job does, build it from the repository's root and set `FLUENTCMS_E2E_IMAGE` to its name; the harness then needs Docker with Linux containers and no publish. Without Docker every case is skipped and says why. The run's containers and volume carry the label `org.fluentcms.e2e` and are removed at its end; a later run removes those a killed run left behind. In PowerShell:

```powershell
docker build -f src/FluentCMS.Host/Dockerfile -t fluentcms-e2e .
```

```powershell
$env:FLUENTCMS_E2E_IMAGE = 'fluentcms-e2e'; dotnet test --project admin/tests/FluentCMS.Admin.EndToEnd -c Release --no-build
```

A new case signs in with `SignedInTabAsync` as its class's editor, or a user it makes when it changes that user, names what it makes with its `Mark`, and waits through the helpers of `Ui` and `MediaUi`: `Ui.PressAsync` and `Ui.FocusAsync` for the keyboard, `tab.AnswerAsync` for an answer, which skips a 401 the admin sends again, `tab.OpenDialogAsync` before reading a dialog, `Ui.DragAsync` for a drag, which lets go once the target shows it takes the drop, and `Ui.UntilAsync` for a state the page reaches on its own. A test class may not read a response's body (`TextAsync`, `JsonAsync`, `BodyAsync`), wait by a fixed delay (`WaitForTimeoutAsync`, `Task.Delay`, `Thread.Sleep`, `setTimeout`), poll against a clock of its own (`DateTime.UtcNow`, `Stopwatch`), which spins requests, drag with `DragToAsync`, or declare its own `PressAsync`; `EndToEndRulesTests` fails the build if it does, and section 7 of the admin technical specification says why. In CI the job runs a failed case once more and names a case that passes then as flaky in its summary; the local run does not.

To run a host on PostgreSQL, or media on S3, `compose.yaml` starts PostgreSQL 15 and MinIO at the versions the tests use, on `localhost` only, with their data in volumes. It is for development, not production. The passwords default to `fluentcms`; set `FLUENTCMS_POSTGRES_PASSWORD` and `FLUENTCMS_MINIO_PASSWORD` to change them. PostgreSQL takes its password when it creates the volume, so set it before the first start or delete the volume.

```bash
docker compose up -d
```

The reference host then runs on PostgreSQL with `FluentCms__Database__Provider=PostgreSql`, `ConnectionStrings__FluentCms=Host=127.0.0.1;Database=fluentcms;Username=fluentcms;Password=fluentcms` and `FluentCms__Database__MigrateOnStartup=true`. The reference host keeps media on the local disk unless `FluentCms__Media__S3__*` is set; it, or a host of your own that calls `media.UseS3`, can use MinIO, which answers S3 on `http://127.0.0.1:9000` (`ForcePathStyle` true) and serves its console on `http://127.0.0.1:9001`, where you create the bucket. `docker compose down` stops both and keeps the data; add `-v` to delete it.

During implementation, run the test project or test class affected by the change. Before opening an issue pull request, into a feature branch or `main`, run the fast checks once:

```bash
dotnet build -c Release -warnaserror
dotnet test --project tests/FluentCMS.Sdk.Tests -c Release --no-build
dotnet test --project tests/FluentCMS.Core.Tests -c Release --no-build
dotnet test --project tests/FluentCMS.Architecture.Tests -c Release --no-build
dotnet test --project admin/tests/FluentCMS.Admin.Tests -c Release --no-build
dotnet test --project tests/FluentCMS.Cli.Tests -c Release --no-build
dotnet test --project tests/FluentCMS.Core.IntegrationTests -c Release --no-build --filter-namespace "FluentCMS.Core.IntegrationTests.Commands*" --minimum-expected-tests 40
npx --yes markdownlint-cli2 "**/*.md"
```

CI runs the full suite, so there is no need to run it locally for an issue pull request. Run the full checks to reproduce a CI failure, or before opening the pull request from a feature branch into `main`:

```bash
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet pack -c Release --no-build -o artifacts/packages
dotnet run eng/check-packages.cs -- artifacts/packages 1.0.0-dev
dotnet publish admin/src/FluentCMS.Admin.App -c Release --no-build -o artifacts/admin
dotnet run eng/check-admin-size.cs -- artifacts/admin 3600000
dotnet run eng/admin-zip.cs -- pack artifacts/admin 1.0.0-dev artifacts/packages
npx --yes markdownlint-cli2 "**/*.md"
```

Publish the admin into an empty folder: files left from an earlier publish count toward the size check. The budget is the one of the `Pack` job in `.github/workflows/ci.yml`.

A test that hangs is named in the diagnostic log of its test app: xUnit.net reports each test that runs longer than two minutes (`tests/xunit.runner.json`), and `--timeout` fails the tests still running with the stack of the call they wait in, if they wait on the test's cancellation token. CI writes the log for every test project and prints these lines when a test job fails or is cancelled. A hang outside a test body, such as a class or collection fixture whose `InitializeAsync` never returns, is not named: it gets no long-running report, and `--timeout` ends the run without a name or a stack. To get the lines locally:

```bash
dotnet test --project tests/FluentCMS.Plugins.Search.Tests -c Release --timeout 10m --diagnostic --diagnostic-verbosity Information --diagnostic-output-directory artifacts/test-results
```

## Decisions

The plan lives in `docs/planning/`. When implementation needs a choice that the [decisions log](docs/planning/specification/12-decisions-log.md) and the backend specification do not already settle, record it in `docs/decisions/` as a short decision record. Open an issue labelled `type:adr` to propose it. The [README](docs/decisions/README.md) there has the details and the [template](docs/decisions/0000-template.md).

## Authorship

Commits and pull requests are authored by the person who submits them. Do not add tool-generated co-author trailers or "generated by" footers. You are responsible for what you submit, whatever tools you used to write it.
