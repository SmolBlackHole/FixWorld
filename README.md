# FixWorld

Privater Performance-Mod für RimWorld `1.6.4871 rev591`. Wir messen zuerst und
testen danach immer nur eine Optimierung gleichzeitig. Eine Weitergabe ist noch
nicht vorgesehen; später folgt ein Pilot-Test mit einer zweiten Modliste.

## Arbeitsweise

- [ROADMAP.md](ROADMAP.md) beschreibt nur die High-Level-Richtung.
- [TODO.md](TODO.md) enthält nur die nächsten konkreten Schritte.
- [docs/dds-cache.md](docs/dds-cache.md) beschreibt Cacheformat, Grenzen und Messregeln.
- [docs/windows-preloader.md](docs/windows-preloader.md) beschreibt den Windows-Frühstart.
- `data/benchmarks/fixtures.csv` identifiziert feste Ausgangssaves.
- `data/benchmarks/results.csv` enthält eine Zeile pro Messlauf.
- Rohlogs, Screenshots, Saves, dekompilierter Code und externe Werkzeuge bleiben
  lokal und sind von Git ausgeschlossen.

## Wichtige Pfade

- `mod/FixWorld/`: Installer, Settings und normale `FixWorld.Mod`-Brücke
- `mod/FixWorld/Source/Runtime/`: frühe Runtime, Loader-Pipeline und Infrastruktur
- `data/benchmarks/saves/spoon-spring-v1.rws`: eingefrorener Ausgangssave
- `data/profiling/captures/`: lokale Messdaten
- `decompiled/Assembly-CSharp/`: read-only Referenz des Zielbuilds
- `tools/dubs-performance-analyzer/`: lokaler Ingame-Profiler

## Befehle

```powershell
python .\tools\build.py
python .\tools\build.py --package
python .\tools\smoke_test.py
python .\tools\benchmark.py
python .\tools\rimworld_process.py
```

Die originalen RimWorld-Binaries bleiben unangetastet. Beim ersten FixWorld-Start wird
der erforderliche Windows-Preloader neben der EXE installiert und RimWorld einmal neu
gestartet. Der eigene Mod, Harmony und Dubs Performance Analyzer sind lokal über
Junctions eingebunden.
