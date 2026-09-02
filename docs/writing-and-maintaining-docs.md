# Writing and maintaining documentation

Parent: [Documentation index](README.md)

Public documentation is written in English. Give every fact one authoritative
owner and link to it instead of copying the same explanation into several files

| Content | Owner |
| --- | --- |
| Product status and first build command | Root `README.md` |
| Documentation navigation | `docs/README.md` |
| Runtime and assembly boundaries | `docs/architecture.md` |
| Setup, checks, benchmarks, and release preparation | `docs/development.md` |
| Windows installation and recovery | `docs/windows-preloader.md` |
| DDS cache behavior | `docs/dds-cache.md` |
| Future direction | `ROADMAP.md` |
| Concrete unfinished work | `TODO.md` |

Every page below `docs/` starts with a `Parent:` link. Describe implemented
behavior in normal prose. Put experiments and unimplemented behavior in the
roadmap or TODO. Run `python tools/check.py` after adding or moving a page
