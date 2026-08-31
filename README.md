# FixWorld

Privater Performance-Mod für RimWorld `1.6.4871 rev591`. Wir messen zuerst und
testen danach immer nur eine Optimierung gleichzeitig. Eine Weitergabe ist noch
nicht vorgesehen; später folgt ein Pilot-Test mit einer zweiten Modliste.

## Arbeitsweise

- [ROADMAP.md](ROADMAP.md) beschreibt nur die High-Level-Richtung.
- [TODO.md](TODO.md) enthält nur die nächsten konkreten Schritte.
- [docs/dds-cache.md](docs/dds-cache.md) beschreibt Cacheformat, Grenzen und Messregeln.
- [docs/windows-preloader.md](docs/windows-preloader.md) beschreibt den optionalen frühen Loader.
- `benchmarks/fixtures.csv` identifiziert feste Ausgangssaves.
- `benchmarks/results.csv` enthält eine Zeile pro Messlauf.
- Rohlogs, Screenshots, Saves, dekompilierter Code und externe Werkzeuge bleiben
  lokal und sind von Git ausgeschlossen.

## Wichtige Pfade

- `mod/FixWorld/`: unser Harmony-Mod
- `benchmarks/saves/spoon-spring-v1.rws`: eingefrorener Ausgangssave
- `profiling/captures/`: lokale Messdaten
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

Die originalen RimWorld-Binaries bleiben unangetastet. Der optionale Windows-Preloader
wird nur nach ausdrücklicher Aktivierung als separate Datei neben der EXE installiert.
Der eigene Mod, Harmony und Dubs Performance Analyzer sind lokal über Junctions eingebunden.
