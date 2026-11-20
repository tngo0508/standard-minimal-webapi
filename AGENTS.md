# AGENTS.md

## Code style: simple over clever

This repo is a learning reference. Code must be easy for a new developer to read and maintain. When there is a choice,
pick the version that is easier to follow, even if it is longer or slightly slower.

- Write one step per line. Give intermediate results a name instead of nesting calls.
- Use plain `if` blocks and early returns. Avoid dense one-liners, nested ternaries and chained null operators.
- Use block bodies (`{ ... }`) for methods that do real work. Expression bodies are fine for trivial one-liners.
- Prefer the ordinary EF Core path (`FindAsync`, `Add`, `Remove`, `SaveChangesAsync`, `Include`) over bulk or
  projection tricks.
- Do not add an abstraction (generic helper, base class, interface, wrapper) until it removes real duplication that
  hurts. A little repetition is better than indirection.
- Do not add security hardening or performance tuning that makes the code harder to read. Mention it in the README as
  something a production system should do.
- Comments explain why, in plain words, wherever a reader new to ASP.NET Core might be puzzled.
- Keep the number of packages, projects and files as small as the feature needs.

## Agent skills

### Issue tracker

Issues and specs live in this repo's GitHub Issues, managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical triage labels are used unchanged: `needs-triage`, `needs-info`, `ready-for-agent`,
`ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.
