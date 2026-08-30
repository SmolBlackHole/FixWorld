# Forschungslog

## 2026-08-30, Initiale Inventur

- Projektwurzel als Forschungs-Workspace bestaetigt: `D:\Projects\RimworldOptim`.
- Steam-Installation read-only untersucht: `G:\Steam\steamapps\common\RimWorld`.
- `Version.txt`: `1.6.4871 rev590`.
- `Assembly-CSharp.dll`: Assembly-/Dateiversion `1.6.9676.17735`.
- `RimWorld.VersionControl` leitet daraus zur Laufzeit `1.6.4871 rev591` ab. Build 4871 stimmt ueberein, die Revision in `Version.txt` ist um eins aelter.
- Unity: `2022.3.35f1`, Player-Build `011206c7a712`.
- Runtime: Unity Mono (`MonoBleedingEdge`), Managed Image Runtime `v4.0.30319`.
- Keine Harmony-DLL und keine Drittanbieter-Mods in der gelieferten Spielwurzel gefunden.
- DLCs Anomaly, Biotech, Ideology, Odyssey und Royalty erkannt. In `Data/` liegen keine DLC-DLLs; der Managed Spielcode ist in `Assembly-CSharp.dll` gebuendelt.
- Globale Werkzeuge: .NET-Runtimes 8.0 und 10.0 vorhanden, aber kein .NET SDK, kein ILSpy/dnSpy und keine .NET-Diagnosetools auf dem PATH.
- Visual Studio Community 2026 ist vorhanden. Die Managed-Desktop-/Roslyn-/Targeting-Pack-Komponenten sind jedoch nicht installiert; nur `MSBuild.exe` allein reicht fuer einen C#-Mod-Build nicht.
- `ilspycmd 11.0.0.9375` lokal aus dem offiziellen NuGet-Paket abgelegt und mit der vorhandenen .NET-10-Runtime ausgefuehrt.
- Vollstaendige Projekt-Dekompilierung erfolgreich: 9.218 Dateien, Exitcode 0, keine ILSpy-Fehlermarker.
- Naechster Schritt: Tick- und Simulationsarchitektur aus dem exakten Build kartieren.

## 2026-08-30, Simulationsarchitektur

- Frame-Einstieg bis `TickManager.DoSingleTick()` und die Reihenfolge der Map-, Thing-, World- und Manager-Ticks nachverfolgt.
- `Normal`, `Rare` und `Long` als gehashte 1-/250-/2.000-Tick-Buckets bestaetigt.
- Pawn-Arbeit in Per-Tick-, dynamische Intervall-, 150-Tick- und 250-Tick-Anteile getrennt.
- Job-/WorkGiver-Scans als ereignisabhaengig beim Jobwechsel eingeordnet, nicht als pauschalen Vollscan in jedem Pawn-Tick.
- RimWorld 1.6 nutzt Unity Jobs/Burst fuer gebuendelte PathRequests; `FindPathNow()` und Reachability besitzen weiterhin synchrone Pfade.
- `MapUpdate()` als pro Frame laufenden Pfad von reinen Simulationsticks getrennt.
- Eingebauten 30-Sekunden-Dev-Benchmark gefunden. Er misst FPS, TPS und Ticks/Frame.
- `Verse.ProfilerBlock` ist in dieser Release-Assembly ein No-op; die sichtbaren Marker liefern ohne Instrumentierung keine Messdaten.
- Architekturkarte erstellt: `notes/simulation-architecture.md`.

## 2026-08-30, Profiling- und Benchmark-Strategie

- Eingebauten Benchmark fuer unverzerrtere End-to-End-Baselines eingeordnet.
- Dubs Performance Analyzer als erstes Diagnosewerkzeug fuer Methodenzeit und Call Counts priorisiert; aktueller Quellstand unterstuetzt RimWorld 1.6.
- Gezielte eigene Harmony-Instrumentierung als zweite Diagnoseebene vorgesehen, nicht als pauschales Deep Profiling.
- `wpr.exe` ist vorhanden, `wpa.exe` fehlt. ETW bleibt optional fuer Hauptthread/Worker/Scheduling.
- Unity Profiler nur als spaeteres Experiment eingestuft; Release-Player und fehlender Unity Profiler machen ihn derzeit unpraktisch.
- `dotnet-trace`/`dotnet-counters` fuer Unity Mono ausgeschlossen.
- Kontrolliertes Protokoll mit festem Save, 3.600-Tick-Warm-up, 30-Sekunden-Fenster, 1x/2x/3x und mindestens drei Wiederholungen dokumentiert.

## 2026-08-30, Harmony-PoC

- Scoop `dotnet-sdk 10.0.400` als einzige erforderliche Build-Toolchain bestaetigt; Visual Studio/Unity nicht erforderlich.
- Vorhandenen offiziellen Workshop-Harmony-Mod `2009463077` erkannt.
- Fuer RimWorld 1.6 wird `Current/Assemblies/0Harmony.dll` geladen: Assembly 2.4.1.0, SHA-256 `353DAAFEC180BB8E7BBE4DA78F2A7CDC78067392E3A4E79DC8E7AF295F2371E6`.
- Harmony-About-Version 2.4.2.0 weicht von der tatsaechlichen DLL-Version 2.4.1.0 ab; der Build referenziert die DLL.
- Separaten `net472`/x64-PoC mit Startlog und verhaltensneutralem `Game.FinalizeInit()`-Postfix erstellt.
- Release-Build zweimal erfolgreich, 0 Warnungen/Fehler, deterministischer DLL-Hash `51372E9496F51D3D223DBD9B5CCD77379EA0862E3A83C1FC051803E043C66DD7`.
- Laufzeittest offen: Noch wurde kein Junction/Mod in der Steam-Installation angelegt.

## 2026-08-30, Isolierter Harmony-Laufzeittest

- Freigegebenen Directory-Junction `G:\Steam\steamapps\common\RimWorld\Mods\RimWorldOptim.Poc` auf den Workspace-Mod angelegt.
- Separaten Datenordner `profiling/poc-userdata/` und separate Mod-Konfiguration verwendet; normale Einstellungen und Spielstaende blieben unberuehrt.
- RimWorld mit `-quicktest` gestartet und automatisch ein Testspiel erzeugt.
- Startmarker und verhaltensneutralen `Game.FinalizeInit()`-Postfix im Log bestaetigt.
- Keine relevanten Harmony-Patch-, Assembly-, `MissingMethod`- oder `TypeLoad`-Fehler gefunden.
- Reproduzierbaren Testlauf als `mod/test-load.ps1` abgelegt.
