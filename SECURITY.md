# Security policy

## Reporting a vulnerability

Report vulnerabilities privately through [GitHub private vulnerability reporting](https://github.com/FluentCMS/fluentcms/security/advisories/new). Do not open a public issue or discuss the problem on Discord.

Include what you found, how to reproduce it, and which branch or version is affected. You will receive an acknowledgement within seven days and updates as the report is triaged and fixed. Credit is given in the advisory unless you ask otherwise.

## Supported versions

| Version | Branch | Status |
| --- | --- | --- |
| v2 | `v2` | Pre-release. No security support until the first tagged release. Reports are still welcome. |
| v1 | `dev` | Security fixes only. No new features. |

## What v2 treats as a security requirement

Agent security is a hard requirement of the v2 design, not an add-on. Each agent has its own revocable credential with scopes and caps, agents cannot approve or publish, content and rule text are treated as untrusted input to every model call, outbound network access is allow-listed, and every security-relevant event is in the audit log. The details are in `docs/requirements/05-non-functional-requirements.md` under SEC. Reports against those requirements are in scope once the corresponding code exists.
