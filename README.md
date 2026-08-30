# RimWorld Performance Research

Lokaler Forschungs-Workspace fuer die installierte RimWorld-Version unter
`G:\Steam\steamapps\common\RimWorld`.

Ziel ist ein reproduzierbarer, messungsgetriebener Ablauf:

1. exakten Build und Laufzeit inventarisieren,
2. Managed Assemblies lokal und nachvollziehbar dekompilieren,
3. Tick- und Simulationsarchitektur kartieren,
4. einen wiederholbaren Profiling-Benchmark aufbauen,
5. erst nach einem gemessenen Engpass eine kleine Harmony-Optimierung pruefen.

## Grenzen

- Spielbinaries und Spieldaten in der Steam-Installation werden als read-only behandelt.
- Ein ausdruecklich freigegebener Junction unter `Mods/RimWorldOptim.Poc` bindet
  den Workspace-Mod reversibel ein.
- Dekompilierter RimWorld-Code und originale DLLs bleiben lokal und werden nicht veroeffentlicht.
- `decompiled/Assembly-CSharp/` und lokale Werkzeug-Binaries sind absichtlich ignoriert.
- Eigener Mod-Code bleibt strikt von Ludeons Code getrennt.
- Es wird nichts optimiert, nur weil es im Quelltext verdaechtig aussieht.

## Struktur

- `notes/`: Befunde, Architekturkarte und Forschungslog
- `tools/`: reproduzierbare Hilfsskripte und lokale portable Werkzeuge
- `decompiled/`: ausschliesslich generierter, urheberrechtlich geschuetzter Code
- `profiling/`: Profiling-Protokoll und lokale Captures
- `benchmarks/`: Benchmark-Protokoll und lokale Ergebnisse
- `mod/`: ausschliesslich selbst geschriebener Harmony-Mod-Code

Generierte Dateien und lokale Maschinenpfade sind in `.gitignore` dokumentiert.
Damit zeigt `git status` nur Aenderungen an unseren versionierbaren Quellen,
Skripten und Notizen.

## Was darf bearbeitet werden?

| Bereich | Umgang |
| --- | --- |
| `mod/RimWorldOptim.Poc/Source/` und `About/` | Eigener Mod-Code und Metadaten, hier wird implementiert |
| `notes/`, `profiling/*.md`, `benchmarks/*.md` | Befunde, Messdesign und Ergebnisse dokumentieren |
| `tools/*.ps1`, `mod/*.ps1`, `mod/test-data/` | Reproduzierbare Werkzeuge und Testvorlagen |
| `decompiled/Assembly-CSharp/` | Nur lesen, durchsuchen und zitieren, niemals als Produktcode bearbeiten |
| `mod/**/obj/`, `bin/`, `Assemblies/` | Generierter Build-Output, nicht bearbeiten oder committen |
| `profiling/poc-userdata/`, `tools/ilspycmd/` | Lokale Laufzeit-/Werkzeugdaten, nicht committen |
| `mod/Local.Build.props` | Lokale Maschinenpfade, bei Bedarf bearbeiten, aber nie committen |

Die Workspace-Einstellungen unter `.vscode/` markieren die generierten und
dekompilierten Bereiche im Editor als schreibgeschuetzt. `RimWorldOptim.slnx`
enthaelt bewusst nur unser PoC-Projekt. Der dekompilierte ILSpy-Projektentwurf
ist keine Build-Solution dieses Repos.

Der aktuelle Stand steht in [notes/research-log.md](notes/research-log.md).

Vertiefende Befunde:

- [Installation und Tooling](notes/installation.md)
- [Tick- und Simulationsarchitektur](notes/simulation-architecture.md)
- [Profiling-Strategie](profiling/strategy.md)
- [Benchmark-Protokoll](benchmarks/protocol.md)
- [Harmony-Eignung und PoC](notes/harmony-feasibility.md)
