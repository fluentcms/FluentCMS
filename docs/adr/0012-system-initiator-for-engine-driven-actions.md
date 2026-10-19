# ADR-0012: A system initiator for actions the product takes on its own

- Status: accepted
- Date: 2026-10-19
- Related: AUD-01, AUD-04, AUD-N2, CHG-01, CHG-04, CHK-03, RTE-01, MCP-06, SEC-02, ADR-0009, ADR-0010

## Context

The audit log names who caused every change, a person or an agent (AUD-01, ADR-0009), and every change set has one of them as its initiator (CHG-01). Some actions have neither behind them:

- The check engine moves a change set from checks running to checks failed or awaiting review once its checks have run (CHK-03, CHG-04). It also proposes checks for a saved rule, which its author then confirms (RUL-04).
- Routing auto-publishes a set that passes on checks (RTE-01, the one exception in MCP-06).
- Maintenance runs on its own, such as clearing stored prompts past their retention (AUD-04, AUD-N2).

These run inside a request or as a job (ADR-0010), and a job runs as whoever enqueued it. For the check run that is the agent that submitted the set, and agents may not make the checks' moves (MCP-06, SEC-02).

## Decision

- A third kind of initiator, `SystemInitiator`, names a part of the product acting on its own by its component: `checks` and `retention`, and `routing` when F-RTE lands. The audit log stores it with the kind `system`, the component as its name, a stable id derived from the component, and no agent credential.
- Its limits are enforced in Core:
  - It is never a change set's initiator. `ChangeSet.Open` refuses it, and so do session grouping and revert, which open sets through it.
  - `AgentPermissions` treats it as not an agent and lets it make only the moves its component owns. `checks` moves a set from checks running to checks failed or awaiting review; routing will add auto-publish.
  - It reads content, and never creates, edits or deletes content, opens or adds to a change set, or calls a tool.
  - What only a person may do refuses a system initiator as it refuses an agent: managing users and issuing their reset links, issuing and revoking credentials, and activating or deactivating checks. An engine cannot write or revise a check either, though an agent may propose one.
  - Maintenance names what it may do: the prompt purge accepts `retention` as well as a person.
  - It spends no model budget of its own. A model call it makes for a change set is made on behalf of whoever opened the set, and an agent's counts against that agent's cap (SEC-04).
- A job type can run as a system initiator, whoever enqueued the job (`IJobHandler.RunAs`). The job still records who enqueued it. The check run is enqueued in the submitting agent's call and runs as `checks`. The job type decides this, not the code that enqueues it, so no caller can borrow an engine's permissions.
- Only Core names its engines and the handlers that run as them. `SystemInitiator` has no public constructor, a stored row reads back only as one of the product's engines, and Core lists each handler that may run as an engine with that engine (`EngineJobs`). The worker fails, without running it, a job whose handler claims an engine it is not listed for, and a job an engine enqueued for a handler that names no engine. A listed handler runs as its own engine whoever enqueued its job, since the job type decides. So a plugin's job handler (EXT-03) never runs as an engine, whatever its `RunAs` says or whoever enqueued its job. Inside a request, only Core opens a scope that acts as an engine, such as the check run of a small set.
- Check types run inside the check engine's scope, a plugin's as well as Core's, so the content they read is read as the engine and what they could do is what the engine may. The engine's limits above bound them; per-plugin permissions come with the plugin SDK (EXT-03).

## Consequences

- The log tells what an agent did apart from what the product did: an agent submits its set, and the check engine, not the agent, moves it on.
- The audit log stores a system initiator by its component and its derived id, and refuses a row where the two disagree, as it refuses an unknown kind.
- Exports and the audit screen show a third initiator kind. Readers of the export handle `system`.
- A new engine moves nothing until its moves are written into `AgentPermissions`, and none writes content.
- AUD-01 and CHG-01 read "agent/user". The system kind covers only engine-driven moves and maintenance, and the maintainer updates that wording in the decision record.

## Alternatives considered

- Act as the submitting agent: no new kind, but false provenance. The log would say the agent moved its own set out of checks running, which it is not allowed to do.
- A service user, an Identity account the engine signs in as: a fake person in the user list and in the log, with a password or key to protect, and "human reviewed" claims would become ambiguous.
- No initiator for these actions: the audit log refuses a change without one (AUD-N1), and a gap would hide who did what.
