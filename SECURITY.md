# Security policy

Parent: [Project README](README.md)

FixWorld installs an early loader beside `RimWorldWin64.exe`, launches a bundled
texture converter, reads mod content, and writes a persistent cache. Reports
about ownership checks, path traversal, unsafe file removal, arbitrary process
execution, restart loops, or malformed cache input should be treated as security
issues.

Do not publish exploit payloads, private saves, authentication data, or complete
player logs in a public issue. Contact the repository owner privately through
the security reporting channel configured on GitHub and include:

- affected FixWorld commit or release;
- RimWorld build and operating system;
- the smallest safe reproduction;
- expected and observed file paths or process behavior;
- whether uninstall or recovery still works.

Only the exact RimWorld build documented in the README is currently supported.
General compatibility problems and performance regressions can use normal
public issues after sensitive data has been removed.
