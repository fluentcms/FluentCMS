# Vision and Principles

## One line

A headless CMS built for an AI agent to operate and for a team to govern: the agent does the work by default, people write the rules and review what matters before it goes live.

## The problem

Every CMS is a form for a person, with AI added as a button. When an agent is the main user, three things break:

1. The agent's rules live in a context file nobody enforces, so it drifts.
2. Facts (prices, dates, names) are copied into every page, so the agent multiplies whatever is already wrong.
3. The review desk is GitHub or an inbox, built for code and email, not for "is this page allowed to say this?"

## What we build

One product that closes the loop: **rule book → agent → checks → review desk → published → back to the rule book.**

- Agents operate the CMS through MCP tools; forms are the fallback for people.
- The rule book is content, edited in plain language and compiled into checks that block violations.
- Facts live once as typed entities with a source and an owner; content references them.
- Every change arrives as a change set with a diff, the agent's reasons and its sources. Routine sets pass on checks; risky sets wait for people; large sets are sampled.
- Every change is logged with the agent, model, prompt and sources that produced it, and the person who approved it.

## Principles (used to settle design arguments)

| # | Principle | Consequence |
|---|-----------|-------------|
| P1 | Tools before forms | Every capability exists as an MCP tool before it has a screen. A screen with no tool is a bug. |
| P2 | Rules are enforced, not suggested | A rule that cannot be checked is labelled advisory and shown as such. Enforced rules block. |
| P3 | A fact lives once | Content never stores a copy of a fact; it stores a reference. Stale or unsourced facts fail checks. |
| P4 | The human gate is the feature | Routing by risk exists to protect reviewer attention, not to remove the reviewer. |
| P5 | Nothing publishes without a record | Provenance (who/what/why/from where) is written before the change is applied, not after. |
| P6 | Any model, any agent | No dependency on one vendor's model or assistant. The owner brings a key or a gateway. |
| P7 | Corrections flow upstream | A reviewer's rejection or edit is an input to the rule book, not a dead end. |
| P8 | Agents are least-privileged | Each agent has its own identity, scopes and caps; the log can always say which agent did what. |
| P9 | Small team, one instance | Design for 3–15 people and thousands of entries. Do not pay for enterprise scale in v1. |
| P10 | Open source, self-hosted | Runs on the team's own infrastructure with no required external service other than the model API. |

## Non-goals (summary; see 07-out-of-scope)

Not a site renderer, not a page builder, not a hosted service, not a v1 upgrade path, not a general agent framework.

## Success looks like

- A team connects an external agent (e.g. Claude Code) on day one and the agent can create types, entries and entities without touching a screen.
- A non-developer edits the rule book and sees which rules are enforced and which are advisory.
- A price change in one entity produces one change set touching every page that references it, routed to a person, approved once, published atomically, revertable in one click.
- Every published item answers: was AI involved, who reviewed it, where did the facts come from.
