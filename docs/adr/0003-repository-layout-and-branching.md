# ADR-0003: Repository layout and branching

- Status: proposed
- Date: 2026-10-16
- Related: D01, LIC-02

## Context

v2 replaces v1 without a migration path (D01), but the project keeps its name, repository, stars, issues and community. v1 is still in use and still receives security fixes. The repository must hold both lines for a while without confusing contributors, and the v1 history and contributor credit should survive.

Two experimental branches exist: `next` (125 commits ahead of `dev`) and `feat/agents-project` (113 ahead). Neither is the base for v2.

## Decision

Branches:

| Branch | Role |
|---|---|
| `dev` | v1, maintenance only. v1 pull requests target it explicitly. |
| `v2` | v2 integration branch, created from `dev` so history is preserved. Becomes the default branch once the bootstrap pull request is merged. All v2 work targets it. |
| `main` | Untouched until M1 ships. Its role for v2 releases is decided then. |
| `archive/next`, `archive/agents-project` | The old experiments, renamed and frozen. |
| `<issue>-<slug>` | Short-lived work branches, one per issue, as v1 already did. |

Workflow: every change starts from an issue. A pull request targets `v2`, references its issue with `Closes #N`, passes CI, and is squash-merged so one issue maps to one commit on `v2`. Commit titles are short and imperative. Commits and pull requests are authored by the person who submits them; no tool-generated co-author trailers or footers.

Issues carry a `v1` or `v2` label, a `type:*` label and, for v2, an `area:*` label matching a requirement group. Milestones M1, M2 and M3 are v2 only.

The first v2 pull request removes `src/`, the v1 docs and workflows in one commit and adds the v2 skeleton. No v1 code is reused.

## Consequences

- One repository, one issue tracker, two lines. Labels and the default branch keep them apart.
- Git history, blame and the contributors graph stay intact.
- After the default switch, v1 contributors must pick `dev` as the base explicitly. The README and the pull request template say so.
- Repository settings (default branch, protection rules) change at one known point: the bootstrap merge.

## Alternatives considered

- Rewrite v2 on `dev` and move v1 to a `v1` branch: breaks every open v1 pull request and every link at once.
- Orphan branch with empty history: cleaner history, but the default-branch contributors graph resets and blame for shared files disappears.
- New repository: splits stars, issues and community, and the name would need qualifying everywhere.
