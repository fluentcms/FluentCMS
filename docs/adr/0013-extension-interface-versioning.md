# ADR-0013: Versioning the extension interfaces

- Status: proposed
- Date: 2026-10-19
- Related: EXT-01, EXT-02, EXT-03, EXT-N1, EXT-N2, ADR-0004

## Context

Every pluggable part of the product is an `[ExtensionPoint]` interface in `FluentCMS.Core`, and the built-in features implement them the way a plugin will (EXT-01, EXT-02). In M1 and M2 only code in this repository implements them. The public plugin SDK comes in M3 (EXT-03), and from then on other people's assemblies compile against these interfaces. EXT-N2 asks that the interfaces be versioned, with breaking changes only at major versions.

Some of what an implementation relies on is not in the interface's signature: what its conformance suite checks, and the names it stores in data, such as a field type's name in a content type definition or a job handler's type in every job.

## Decision

- **The contract of an extension point** is three things:
  - its `[ExtensionPoint]` interface and the public types in its members;
  - the behaviour its conformance suite checks (EXT-N1);
  - the names it stores, which never change once released.
- **Additive changes** keep every implementation compiling and passing its suite:
  - a new extension point;
  - a new member with a default implementation;
  - a new optional argument on a record or options type that a caller fills in;
  - a new type that existing members don't return.
- **Breaking changes**:
  - removing or renaming a member;
  - changing a member's signature, or the nullability of its types;
  - a new member without a default;
  - a stricter conformance check;
  - a new value an implementation must handle in a closed set it switches on, such as an enum, unless the contract says unknown values are ignored;
  - renaming a stored name.
- **Before the SDK ships (M1 and M2)**, any change is allowed, because every implementation is in this repository and is changed in the same pull request. Each change still updates the baseline below and says in the pull request whether it is additive or breaking, so the M3 SDK starts from a known surface.
- **From the SDK release**:
  - The interfaces follow the product's semantic version. A minor release takes only additive changes. A breaking change waits for the next major.
  - A member to be removed is first marked `[Obsolete]` with its replacement, for at least one minor release, and goes at the next major.
- **A surface baseline** in `tests/FluentCMS.Tests/Extensions/extension-points.txt` lists every extension point and its members, with their types and nullability.
  - A test compares the interfaces with it and names each member that differs.
  - A pull request that changes an extension point updates the baseline, and its reviewer judges the change against the rules above.
- **Every extension point has a conformance class**: an abstract test class that every implementation derives from, built in or not, marked `[ConformanceFor(typeof(...))]`. A test fails for an extension point that none covers.

## Consequences

- A change to an extension point can't slip through review unseen. The baseline diff shows it, and the rule test names it.
- A new extension point lands with its conformance class, so a plugin author has the contract to test against from the start.
- The baseline covers the interfaces' own members. A change to a type that only appears in a signature, such as `JobContext`, shows up in that type's own review, not in the baseline.
- Stored names are part of the contract, so renaming a built-in field type, check type, tool or job type is breaking even when no signature changes.

## Alternatives considered

- **`Microsoft.CodeAnalysis.PublicApiAnalyzers`** with `PublicAPI.Shipped.txt`: it tracks the whole public surface of `FluentCMS.Core`, not just the extension points. It adds a package and is noisy for a library that isn't published yet. The baseline test tracks only what plugins implement.
- **A separate contracts assembly**: the cleanest way to version the interfaces apart from the rest of Core, but ADR-0004 keeps M1 to a few projects, and nothing is published until M3. M3 can still move the interfaces without breaking source compatibility.
- **No rule before M3**: simplest, but the SDK would start from whatever the interfaces drifted to, with no record of which changes were breaking.
