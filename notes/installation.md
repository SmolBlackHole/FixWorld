# Installations- und Laufzeitinventur

Stand: 2026-08-30

## Spiel und Engine

| Merkmal                                 | Befund                         | Quelle                                                       |
| --------------------------------------- | ------------------------------ | ------------------------------------------------------------ |
| Installations-Textdatei                 | `1.6.4871 rev590`              | `Version.txt`                                                |
| Zur Laufzeit berechneter RimWorld-Build | `1.6.4871 rev591`              | `RimWorld.VersionControl` plus Assembly-Version              |
| Assembly-Version                        | `1.6.9676.17735`               | CLR-Assemblymetadaten und Dateiversion                       |
| Unity                                   | `2022.3.35f1 (011206c7a712)`   | `RimWorldWin64.exe`, `UnityPlayer.dll`, `globalgamemanagers` |
| Scripting Runtime                       | Unity Mono, nicht IL2CPP       | `MonoBleedingEdge/` und Managed IL-Assemblies vorhanden      |
| CLR Image Runtime                       | `v4.0.30319`                   | `Assembly-CSharp.dll`                                        |
| Unity `mscorlib`                        | `4.6.57.0`, Assembly `4.0.0.0` | `Managed/mscorlib.dll`                                       |

`RimWorld.VersionControl` subtrahiert 4805 vom Assembly-Build und berechnet die
Revision ganzzahlig als `AssemblyRevision * 2 / 60`. Aus `1.6.9676.17735` folgt
damit `1.6.4871 rev591`. `Version.txt` ist bei gleicher Buildnummer um eine
Revision aelter. Fuer Code- und Benchmark-Zuordnung ist deshalb der DLL-Hash die
eindeutige Kennung.

## Primaere Managed Assemblies

- Spielcode: `RimWorldWin64_Data\Managed\Assembly-CSharp.dll`
- Fruehe Unity-Komponenten: `Assembly-CSharp-firstpass.dll`
- Unity-API: `UnityEngine.CoreModule.dll` plus modulare `UnityEngine.*.dll`
- Framework: `mscorlib.dll`, `System.dll`, `System.Core.dll`, `netstandard.dll` und weitere System-Assemblies
- Weitere direkte Spielabhaengigkeiten: Steamworks.NET, NAudio, NVorbis, SharpZipLib, Unity Burst/Collections/Mathematics/TextMeshPro

## Hashes

| Datei                        | SHA-256                                                            |
| ---------------------------- | ------------------------------------------------------------------ |
| `Assembly-CSharp.dll`        | `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A` |
| `UnityPlayer.dll`            | `E0C489F1683609247FEDE45EA049D30BAA4F4542060E308E25C0EC87F6C0FB96` |
| `mono-2.0-bdwgc.dll`         | `D2F4348A5AA80BBDD0E73582CC2A00B3A17FE4A497DA7052436E780CDEE2A0FA` |
| `mscorlib.dll`               | `62EA68DAF78ED2CC20CF34921DE70CE6DEBF33CF3B59FFA79F4DED29EF5FCFE9` |
| `UnityEngine.CoreModule.dll` | `C5C58EA254834291780A1D6C388C241443D07167B4A4B890A23C9494F626DDBA` |

## DLC und Modding-Komponenten

Erkannt wurden Core sowie Anomaly, Biotech, Ideology, Odyssey und Royalty. Unter
`Data/` existieren keine separaten Managed DLC-DLLs. Die Spielwurzel enthaelt
weder `0Harmony.dll` noch HugsLib oder Dubs Performance Analyzer. `Mods/`
enthaelt nur Ludeons Platzhalterdatei.

Diese Aussage gilt nur fuer die gelieferte RimWorld-Spielwurzel. Der spaeter aus
der Steam-Bibliotheksstruktur abgeleitete Workshop-Pfad enthaelt den offiziellen
Harmony-Mod `2009463077`, aber derzeit keinen Dubs Performance Analyzer
`2038874626`.

## Lokale Entwicklungswerkzeuge

| Werkzeug                            | Status                 | Bewertung                                                                         |
| ----------------------------------- | ---------------------- | --------------------------------------------------------------------------------- |
| .NET Runtime                        | 8.0.27 und 10.0.9      | ausreichend fuer portable ILSpy-CLI                                               |
| .NET SDK                            | Scoop `10.0.400`        | fuer den Mod-Build erfolgreich verwendet                                          |
| Visual Studio                       | nicht benoetigt         | der Build verlaesst sich nicht auf VS-Komponenten                                 |
| Roslyn/Managed-Desktop-Workload     | nicht benoetigt         | Compiler kommt aus dem Scoop-SDK                                                  |
| .NET Framework 4.7.2 Referenzen     | NuGet-Buildpaket `1.0.3` | projektlokal wiederhergestellt, kein System-Targeting-Pack noetig                |
| ILSpy/dnSpy                         | fehlt                  | ILSpy wird lokal-portabel genutzt, dnSpy ist optional                             |
| Rider                               | fehlt                  | nicht erforderlich                                                                |
| Unity Editor                        | fehlt                  | nicht erforderlich fuer Assembly-Analyse oder Harmony-Mod                         |
| dotnet-trace/counters, PerfView     | fehlen                 | spaetere optionale Profiler, nicht fuer Dekompilierung erforderlich               |

Der bestehende System-`dotnet`-Host liegt in dieser laufenden Sitzung vor dem
Scoop-Pfad und besitzt kein SDK. `mod/build.ps1` erkennt deshalb das Scoop-SDK
ueber `scoop prefix dotnet-sdk`. Ein neues Benutzerterminal sollte den von Scoop
aktualisierten PATH uebernehmen.

Windows setzt hier den Maschinen-PATH vor den Benutzer-PATH. Deshalb gewinnt
auch in einem neuen Prozess der verwaiste Host unter `C:\Program Files\dotnet`,
obwohl der Scoop-SDK am Anfang des Benutzer-PATH steht. Fuer VS Code wurden
deshalb zwei explizite Benutzereinstellungen gesetzt:

- die .NET-Erweiterungen verwenden
  `P:\Dev\Scoop\apps\dotnet-sdk\current\dotnet.exe`,
- neue integrierte Terminals stellen den Scoop-SDK vor den geerbten PATH.

VS Code muss nach dieser Aenderung vollstaendig beendet und neu gestartet
werden. Ein neues integriertes Terminal muss fuer `dotnet --list-sdks` den SDK
`10.0.400` anzeigen.

`mod/**/obj/` ist generierter NuGet-/MSBuild-Zustand und darf nicht formatiert
oder manuell bearbeitet werden. Ein zwischenzeitlich vom Editor umgebrochenes
`ProjectAssetsFile` in `*.nuget.g.props` verursachte `NETSDK1004`; ein
`dotnet restore --force-evaluate` stellte die Datei wieder her. Die
Workspace-Einstellungen markieren `obj/` nun als schreibgeschuetzt.

## Mod-Ziel

Der verifizierte PoC kompiliert fuer `net472` und `x64`. Dies entspricht auch
dem aktuellen RimWorld-1.6-Projekt von Dubs Performance Analyzer. Die erzeugte
DLL referenziert exakt die lokale RimWorld-Assembly und die fuer 1.6 geladene
Workshop-Harmony-Assembly.
