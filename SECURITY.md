# Security policy

Tether accepts network connections from other players, so security bugs matter. They could let someone
crash a host, take over a session, or affect a player's machine.

## Reporting

Report vulnerabilities **privately** through GitHub's
[private vulnerability reporting](../../security/advisories/new). Don't open public issues for them.
Include:

- the Tether version and game build;
- steps to reproduce;
- what an attacker gains.

## Scope

**In scope:**

- crashes or hangs triggered by network input;
- memory or CPU exhaustion from hostile peers;
- bypassing the session password;
- impersonating another player;
- a host making a client run code or write files outside the game's data folders.

**Out of scope for v1:**

- Eavesdropping on the direct UDP transport. It is unencrypted, and documented as being for LAN and
  trusted use.
- Cheating by the host. The host is the authority by design.

## Design notes

See `docs/PLAN.md` §9 for the security model.
