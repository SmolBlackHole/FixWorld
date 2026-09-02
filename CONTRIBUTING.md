# Contributing to FixWorld

Parent: [Project README](README.md)

FixWorld changes an early, compatibility-sensitive RimWorld path. Keep changes
small, measurable, and explicit about ownership.

## Before changing code

Read the [architecture](docs/architecture.md), [development guide](docs/development.md),
and current [TODO](TODO.md). Do not add a second loading path, speculative shared
abstraction, or compatibility wrapper without an approved design decision.

## Change requirements

- Preserve the active mod list, mod order, produced definitions, and save behavior.
- Keep Unity and mutable Verse state on the main thread.
- Put external processes, Harmony calls, and RimWorld calls behind their existing owner.
- Add or update focused contract tests when a Shared primitive changes.
- Measure performance claims against a reproducible baseline.
- Keep public documentation in English and future behavior in the roadmap or TODO.

## Verification

Run:

```powershell
python .\tools\check.py
python .\tools\build.py
```

RimWorld-dependent changes also require a full-mod-list launch to the main menu.
State the supported build, mod list, cache state, checks run, and any skipped
in-game verification in the pull request.

Do not attach RimWorld assemblies, decompiled source, saves, private logs, or
third-party mod binaries to public issues or pull requests.
