# FluentCMS v2 documentation

| Folder | What it holds |
|---|---|
| [`requirements/`](requirements/) | What v2 must do. Owned by the product side; changed through issues, not feature pull requests. |
| [`adr/`](adr/README.md) | Why v2 is built the way it is. Architecture decision records, one file per decision. |

## Requirements

Read them in this order:

1. [Vision and principles](requirements/01-vision-and-principles.md): the one-line pitch, the problem, the ten principles used to settle design arguments.
2. [Decision record](requirements/00-decision-record.md): the product decisions D01 to D27, and the questions still open.
3. [Personas and scenarios](requirements/02-personas-and-scenarios.md): who uses it, which agents connect, and the product catalog reference scenario.
4. [Feature list](requirements/03-feature-list.md): features by milestone with MoSCoW priority.
5. [Functional requirements](requirements/04-functional-requirements.md): numbered requirements by group (SCH, CNT, ENT, CHG, RUL, CHK, RTE, DSK, MCP, AGT, AUD, API, FRM, ADM, MED, USR, EXT, I18, MST, SRC).
6. [Non-functional requirements](requirements/05-non-functional-requirements.md): security, provenance, AI disclosure, model independence, performance, reliability, operations, usability.
7. [Glossary](requirements/06-glossary.md): the vocabulary used everywhere else.
8. [Out of scope](requirements/07-out-of-scope.md): what is deferred and what is never part of this product.

Requirement IDs are cited in issues, ADRs and code comments. Issue labels `area:*` map to the requirement groups.

## Architecture decisions

The [ADR index](adr/README.md) lists every record with its status. ADR-0001 describes the format and the process for proposing a new one.

User and developer documentation will be added as features land.
