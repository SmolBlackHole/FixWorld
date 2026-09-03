# FixWorld documentation

Parent: [Project README](../README.md)

Use this index after the root README has explained the project and its current
support boundary

## Choose a starting point

| Goal | Start here |
| --- | --- |
| Understand process entry and runtime ownership | [Architecture](architecture.md) |
| Understand the owned loading stages and XML cache | [Play-data pipeline](play-data-pipeline.md) |
| Understand telemetry, benchmark data, and the in-game UI | [Runtime diagnostics](diagnostics.md) |
| Build, test, benchmark, or package FixWorld | [Development and verification](development.md) |
| Understand or remove the Windows early loader | [Windows loader](windows-preloader.md) |
| Understand the DDS cache and its limits | [DDS texture cache](dds-cache.md) |
| Add or move public documentation | [Writing documentation](writing-and-maintaining-docs.md) |
| Inspect reverse-engineering provenance | [Decompiled reference policy](../decompiled/README.md) |
| See future direction | [Roadmap](../ROADMAP.md) |
| See concrete open work | [TODO](../TODO.md) |
| Contribute a change | [Contributing](../CONTRIBUTING.md) |
| Report a vulnerability | [Security policy](../SECURITY.md) |
| Review redistributed dependencies | [Third-party notices](../THIRD_PARTY_NOTICES.md) |

## Documentation ownership

The root README owns the short product introduction and first build command.
This index owns navigation. Architecture owns runtime boundaries. The play-data
page owns implemented loading behavior and measured stage boundaries. Runtime
diagnostics owns telemetry and UI behavior. Development owns commands and
verification. Future work belongs only in the roadmap or TODO, never in prose
that describes implemented behavior
