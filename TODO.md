# TODO

Aktuell: **Feature Freeze. Vor weiteren Loader-Features werden die vorhandenen Infrastrukturgrenzen sauber gezogen.**

Der abgeschlossene Stand gehört in Git, Benchmarks und Logs. Diese Datei enthält nur noch bewusst verschobene Arbeit.

## Aktueller Infrastruktur-Refactor

- [x] `texconv` ausschließlich über einen kleinen typisierten Wrapper aufrufen; Aufrufer übergeben nur Eingabepfade, Ausgabeverzeichnis und DDS-Anforderungen
- [x] Eingabedaten an der Tool-Grenze nur als Pfade beziehungsweise `--file-list` übergeben; der Wrapper kopiert keine Assetdaten
- [x] DDS-Cache-Cleanup in das C#-Tool übernehmen und das PowerShell-Skript entfernen
- [ ] die Hash-Staging-Kopien des DDS-Builders nur mit einer kollisionssicheren direkten Ausgabestrategie entfernen; der Tool-Wrapper selbst ist bereits kopierfrei
- [ ] festlegen, welche Verträge wirklich assemblyübergreifend in `Shared` leben; zentrale Runtime-Dienste sind nicht automatisch Shared-Code
- [ ] Caching, Scheduling, Worker und Runtime-Initialisierung anhand ihrer tatsächlichen Besitzer und Lebenszeiten neu zuschneiden
- [ ] RimWorlds vorhandenes Unity Job System mit einem isolierten `IJob`-/`NativeArray`-Prototyp prüfen, bevor FixWorld einen eigenen Worker-Unterbau festlegt
- [ ] erst nach dem Prototyp entscheiden, welche Arbeit Unity Jobs, FixWorld-Worker oder der Main Thread ausführen

## Nächster Schnitt: eigener Mod-Boot

- [x] `FixWorld.Runtime.dll` mit idempotentem Start-, Attach- und Shutdown-Vertrag früh laden
- [x] fehlende oder inkompatible Runtime ohne FixWorld-Hooks auf Vanilla zurückfallen lassen
- [x] EventBus, Scheduler, Dispatcher und Mod-Boot aus Loader und normaler Mod in die Runtime verschieben
- [x] den normalen Mod auf einen einmaligen Doorstop-Installer mit automatischem Neustart reduzieren
- [x] bei aktiviertem, aber inaktivem Doorstop ohne vererbte Prozessmarke sicher abbrechen und Neustart-Schleifen verhindern
- [x] den normalen FixWorld-Runtime-Start nur nach nachgewiesener Übernahme von `LoadAllActiveMods()` erlauben
- [x] `FixWorld.Loader.dll` nach dem Laden von `Assembly-CSharp` über Doorstop starten
- [x] RimWorld-Version, Assembly-MVID und `LoadAllActiveMods(bool)`-Signatur vor der Übernahme prüfen
- [x] `LoadedModManager.LoadAllActiveMods()` vollständig durch den FixWorld-Coordinator ersetzen
- [x] Hauptassembly in `FixWorld.Mod.dll` umbenennen und alte `FixWorld.dll` aus Build und Paket entfernen
- [x] sicherstellen, dass Runtime und Modassembly jeweils genau einmal geladen werden
- [x] `ModContentPack` und Settings-Daten über den typisierten `AttachMod()`-Snapshot anbinden
- [x] den Mod-Boot als lineare `ModBootPipeline` mit einem Run-Kontext und zentralem Stage-Runner abbilden
- [x] `InitializeMods()` als eigene Stage für Metadaten und `ModContentPack` übernehmen
- [x] noch nicht übernommene RimWorld-Schritte hinter ehrlichen Stage-Adaptern kapseln
- [x] `PrepareModContent` als eigene Stage vor XML modellieren und von der später ausgeführten Asset-Content-Stage trennen
- [x] alle übernommenen Stages über einen gemeinsamen typisierten Stage-Event-Vertrag an UI, Telemetrie und Benchmarks anbinden
- [x] exakt zuordenbare untergeordnete Mod-Operationen erfassen und gleichartige DeepProfiler-Doppelzählung für typisierte Arbeit unterdrücken
- [x] frühen Mod-Boot und spätere LongEvent-Arbeit auf denselben Event- und Telemetrievertrag führen, ohne sie in einen ungeeigneten gemeinsamen Executor zu zwingen
- [ ] Assembly-Discovery und Assembly-Loading als eigene Stages übernehmen und typisiert messen
- [ ] die heutige Mischstage `LoadModContent()` in Assembly-Load und nur eingereihte Asset-Arbeit zerlegen und pro Mod erfassen
- [ ] `CreateModClasses()` vollständig übernehmen und Konstruktor- sowie Harmony-Zeiten erfassen
- [ ] Ursache des langen frühen `...`-Abschnitts mit dieser Telemetrie belegen
- [ ] `GetAllFilesForModPreserveOrder()` und Assembly-Discovery in die eigene Stage-Pipeline übernehmen
- [ ] LongEvent-Thread, synchrone Events, Szenenwechsel und Exception-Lebenszyklus als FixWorld-Vertrag erfassen
- [ ] `MainMenuReady` nach `Menü -> Spiel -> Menü` in einem realen Save-Lauf erneut auslösen und verifizieren
- [ ] RimWorld- und Harmony-Operationen nur noch hinter expliziten FixWorld-Adaptern aufrufen

### Vollständige Play-Data-Pipeline

- [ ] `PlayDataLoader.DoPlayLoad()` als obersten FixWorld-Pipeline-Einstieg übernehmen, damit nicht nur `LoadAllActiveMods()` unter eigener Kontrolle steht
- [ ] die neue `PlayDataLoadPipeline` als eine geordnete Folge expliziter Stages modellieren:
  - [ ] Initialize mods
  - [ ] Prepare mod content
  - [ ] Create mod classes
  - [ ] Load and patch XML
  - [ ] Parse definitions
  - [ ] Resolve definitions
  - [ ] Generate implied definitions
  - [ ] Execute deferred main-thread work
  - [ ] Finalize static initialization
  - [ ] Complete
- [ ] Stage-Fortschritt, Fehler und Laufzeiten ausschließlich über typisierte Observer veröffentlichen; Pipeline-Code schreibt weder UI noch Benchmark-Reports direkt
- [ ] Worker-Vorbereitung und geordneten Main-Thread-Commit über die Shared-Scheduling-Verträge abbilden
- [ ] nach dem verifizierten Cutover die Delegate-Rekonstruktion aus `VanillaDelayedActionBridge`, `VanillaLoadingActionAdapter`, `LoadingCoordinator`, `LoadingStageExecutor` und `LoadingWork` vollständig entfernen
- [ ] vollständige Modliste, Quarry-Save, Hauptmenüabschluss und Benchmark-Ausgabe vor dem Löschen des alten Pfads verifizieren

## Loader und Worker

- [ ] vorhandene RimWorld-Parallelisierung im Def-Aufbau erfassen, bevor FixWorld zusätzliche Worker einsetzt
- [ ] Parallelität je Stage bestimmen; DDS-Validierung war mit vier Workern schneller als mit acht
- [ ] DDS-Build mit zwei, vier und acht Workern vergleichen
- [ ] Texturvorbereitung von Unity-Erzeugung, `Apply`, Kompression und Upload trennen
- [ ] Renderpausen und reine Wall-Time pro framefähiger Stage getrennt berichten
- [ ] XML-Patches, Def-Auflösung, Reflection und Harmony-Scanning einzeln bewerten
- [ ] statische Konstruktoren weiterhin geordnet übernehmen und nur nach mod-spezifischem Nachweis optimieren
- [ ] Discovery, Cache-Validierung und weitere reine Byte-Verarbeitung als Worker-Kandidaten messen
- [ ] Workerfehler abbrechen oder kontrolliert auf den sequenziellen Originalpfad zurückführen
- [ ] deterministische Ergebnis- und Commit-Reihenfolge über wiederholte Läufe prüfen
- [ ] Worker-Anzahl gegen CPU-Kerne, Speicherdruck sowie NVMe, SATA und HDD benchmarken
- [ ] RAM-, VRAM-, Queue- und GC-Spitzen pro Stage erfassen
- [ ] RimWorld- und Harmony-Hooks weiter auf dünne Übersetzer in FixWorld-Jobs reduzieren
- [ ] Telemetrie-Hochrechnung gegen GC- und Scheduling-Ausreißer robust machen

## Cache und DDS

- [ ] den generischen Cache-Core erst erweitern, wenn ein zweiter echter Cache ein Backend oder eine Policy gemeinsam nutzen kann
- [ ] Dimensionen, Hashes und vorbereitete Texturpläne nur bei nachgewiesenem Nutzen im Speicher cachen
- [ ] Cache-Misses als deduplizierbare Producer-Jobs an den Scheduler übergeben
- [ ] DDS-Background-Arbeit anhand von CPU-, I/O- und TPS-Budget drosseln oder pausieren
- [ ] Background-Fortschritt und verbleibende Assets für UI, Logs und Benchmarks bereitstellen
- [ ] Plattform-Backend in die Cache-Identität aufnehmen, sobald ein zweites Backend existiert
- [ ] BC3-DDS gegen unkomprimierte DDS vergleichen
- [ ] PNG/JPG begrenzt parallel dekodieren und nur fertige Daten geordnet übernehmen

## Benchmark und Pilotbetrieb

- [ ] Preloader für Benchmarks explizit schaltbar machen, statt den Installationszustand zu erben
- [ ] PNG/JPG und DDS jeweils mit kaltem und warmem OS-Dateicache messen
- [ ] je drei NVMe-Kontrollläufe mit 0 und 256 MiB Read-ahead vergleichen
- [ ] HDD-Pilot mit großer Modliste bei 0, 256, 512 und 1.024 MiB durchführen
- [ ] parallele Discovery und Read-ahead auf HDD testen; Suchzeit und Durchsatz getrennt messen
- [ ] vollständiges Assembly- und Harmony-Laden erst nach der frühen Instrumentierung bewerten

## Ingame

- [ ] eingefrorenen komplexen Save zweimal messen
- [ ] dominanten Tick-Pfad bestimmen und genau eine Optimierung per A/B-Test bewerten
- [ ] `TickManager`, `MapPreTick`, `MapPostTick`, Unity-Jobs, FixWorld-Worker und Hauptthreadzeit getrennt messen
- [ ] Background-Jobs anhand von TPS, Framezeit sowie CPU- und I/O-Druck drosseln
- [ ] RimThreadeds Muster nur auf nachgewiesene RimWorld-1.6-Hotpaths übertragen, keine alten Patches portieren

### Pathfinding-Slice

- [ ] vorhandene RimWorld-1.6-Path-Jobs instrumentieren, nicht in den FixWorld-Worker-Pool verschieben
- [ ] `PushRequest`, `FindPathNow`, Queue-Latenz, Requests pro Tick, Batchgröße und `MapGridRequest`-Wiederverwendung erfassen
- [ ] vollständige und inkrementelle `PathFinderMapData`-Updates getrennt messen
- [ ] Path-Requests nach Pawn, Ziel, Traversal-Modus und Kostenprofil erfassen
- [ ] Türen, Gefahren, Feuer, Reservierungen, Terrain, Gebäude, Hindernisse, Regionen und Zonen als Abhängigkeiten untersuchen
- [ ] Reachability und `ReachabilityCache` getrennt vom eigentlichen PathFinder profilieren
- [ ] erst danach Path-Reuse und gestufte Path-Caches mit präziser Invalidierung testen
- [ ] Zeit pro Request, expandierte Nodes, Pfadlänge, Worst-Case-Nodes, Hit-Rate und Invalidierungen berichten

## Später

- [ ] DDS-Pack erst nach einer direkten Byte- oder Stream-Ladegrenze erneut bewerten
- [ ] OBST als mögliches Packformat mit Sidecar-Index prüfen
- [ ] GPU-Dekodierung, Mipmaps und Uploads erst nach sauberer CPU-Aufteilung bewerten
- [ ] Linux-Konverter und Plattform-Fallback bauen
