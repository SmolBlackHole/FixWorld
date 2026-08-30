# RimWorldOptim

Kleiner Forschungs-Mod für RimWorld `1.6.4871 rev591`. Wir messen zuerst und
testen danach immer nur eine Optimierung gleichzeitig.

## Arbeitsweise

- [ROADMAP.md](ROADMAP.md) beschreibt nur die High-Level-Richtung.
- [TODO.md](TODO.md) enthält nur die nächsten konkreten Schritte.
- `benchmarks/fixtures.csv` identifiziert feste Ausgangssaves.
- `benchmarks/results.csv` enthält eine Zeile pro Messlauf.
- Rohlogs, Screenshots, Saves, dekompilierter Code und externe Werkzeuge bleiben
  lokal und sind von Git ausgeschlossen.

## Wichtige Pfade

- `mod/RimWorldOptim.Poc/`: unser Harmony-Mod
- `benchmarks/saves/spoon-spring-v1.rws`: eingefrorener Ausgangssave
- `profiling/captures/`: lokale Messdaten
- `decompiled/Assembly-CSharp/`: read-only Referenz des Zielbuilds
- `tools/dubs-performance-analyzer/`: lokaler Ingame-Profiler

## Befehle

```powershell
.\mod\build.ps1
.\mod\test-load.ps1
```

Die RimWorld-Binaries bleiben unangetastet. Der eigene Mod und Dubs Performance
Analyzer sind ausschließlich über lokale Junctions eingebunden.
