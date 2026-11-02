# Decision records

This folder holds short architecture decision records (ADRs) for choices made while implementing FluentCMS.

Write an ADR only for a choice that is not already settled in the [decisions log](../planning/specification/12-decisions-log.md) or in section 10.1 of the [backend technical specification](../planning/backend-technical-spec.md). If the plan already answers the question, follow the plan; if you think it is wrong, open an issue instead of an ADR that contradicts it.

## Proposing a decision

1. Open an issue labelled `type:adr` describing the choice and the options.
2. Once a maintainer has confirmed the direction, copy [`0000-template.md`](0000-template.md) to the next free number and fill it in.
3. Open a pull request that adds the record and says `Closes #<issue-number>`.

## File names

Records are named `NNNN-short-slug.md`, numbered from `0001` in the order they are proposed, for example `0001-storage-spike-outcome.md`. A number is never reused.

## Statuses

| Status | Meaning |
| --- | --- |
| Proposed | Under discussion in its issue and pull request. |
| Accepted | Agreed and in effect. |
| Superseded | Replaced by a later record, named in the status line. |

An accepted record is not edited to change its decision. Write a new record that supersedes it, and update the old record's status to point to the new one.
