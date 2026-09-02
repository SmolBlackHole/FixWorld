# TODO

Aktuell gilt **Feature Freeze**. FixWorld besitzt den frühen Runtime-Start und die
Orchestrierung von `PlayDataLoader.DoPlayLoad()`. Neue Optimierungen beginnen erst,
wenn der betroffene Bereich gemessen und sein bestehender Code auf eine eindeutige
Ownership reduziert wurde.

Diese Datei enthält nur offene Arbeit. Erledigte Migrationen gehören in Git,
Benchmarks und Logs.

## Arbeitsregeln

- immer genau einen aktiven Ladepfad behalten und den dadurch ersetzten Altcode im selben Schnitt entfernen
- RimWorld-, Harmony-, Unity- und Tool-Aufrufe hinter einem eindeutigen Owner halten
- Shared-Code nur für echte assemblyübergreifende Verträge verwenden
- Worker bereiten reine Daten vor; Unity- und Verse-Zustand bleibt geordnet im Main Thread
- jede Verhaltensänderung mit vollständiger Modliste und typisiertem Benchmark prüfen

## Verifizierter Stand

- Doorstop, Loader, Runtime und Mod haben getrennte Boot- und Attachment-Verantwortung
- FixWorld orchestriert alle 15 Play-Data-Stages und besitzt Lifecycle, Scheduling und Telemetrie
- der Python-Benchmark startet RimWorld, wartet, validiert und aggregiert die von der Runtime geschriebene JSON
- Shared stellt isolierte Caching-, Scheduling-, Profiling- und Event-Primitiven bereit; DDS läuft deferred und startet `texconv` nur über den Tool-Wrapper

## Aktive Reihenfolge

1. DDS-Subsystem verhaltensgleich verkleinern und seine Ownership schärfen
2. DDS-Cache nach der sRGB-Identitätsänderung neu aufbauen und warmen 88-Mod-Baseline-Lauf erfassen
3. einen Runtime-Diagnosesnapshot und ein kompaktes Startup-Summary bereitstellen
4. dominantes Deferred Work analysieren und erst danach gezielt zerlegen
5. ein kleines read-only Diagnosefenster auf denselben Snapshot setzen
6. verbleibende RimWorld-Operationen Stage für Stage tiefer übernehmen
7. erst danach neue Worker- oder Format-Experimente aktivieren

## DDS und Texture Cache

Ziel: Ein Runtime-Dienst steuert den Ablauf. Planner, Builder und Store besitzen
jeweils genau eine fachliche Aufgabe. Der Schnitt soll Code entfernen und keine
neue generische Cache-Plattform erfinden.

### Verhaltensgleicher Reduktionsschnitt

- [ ] `TextureDdsCache` und `TextureDdsCacheRuntime` zu einem RuntimeContext-eigenen Dienst zusammenführen
- [ ] Planner auf einen unveränderlichen `TextureCachePlan` reduzieren und doppelte Übergabemodelle entfernen
- [ ] Builder nur konvertieren lassen; Store allein Index, Packs, atomare Veröffentlichung und Recovery besitzen lassen
- [ ] `TextureDdsCacheBackground` auf Scheduler-Jobs, Abbruch und geordnete Veröffentlichung eines Plans begrenzen
- [ ] verwaiste `.staging-*`-Verzeichnisse bereinigen und Hash-Staging-Kopien nur kollisionssicher entfernen
- [ ] Konfiguration, Metriken und Report-Snapshot jeweils nur an einer Stelle modellieren

Akzeptanz:

- [ ] keine statische globale Cache-Instanz und keine reine Durchreiche-Ebene
- [ ] Cache-Identität und Ergebnisse bleiben identisch; Neuaufbau, Warmstart, Abbruch und Neustart funktionieren bei benutzbarem Menü

### Cache-Policy und Experimente

- [ ] Cache-Misses als deduplizierbare Producer-Jobs an den Scheduler übergeben
- [ ] Background-Arbeit anhand von CPU-, I/O-, RAM- und TPS-Budget drosseln oder pausieren
- [ ] Background-Fortschritt und verbleibende Assets für UI, Logs und Benchmarks bereitstellen
- [ ] In-Memory- und generischen Cache-Core nur bei einem zweiten gemessenen Anwendungsfall erweitern
- [ ] BC3, unkomprimierte DDS und BC7-GPU-Kompression getrennt nach Qualität, Größe, Erstellzeit und Unity-Kompatibilität vergleichen
- [ ] DDS-Pack erst nach einer direkten Byte- oder Stream-Ladegrenze erneut bewerten
- [ ] OBST als mögliches Packformat mit Sidecar-Index prüfen

## Deferred Main-Thread Work

Der aktuelle 88-Mod-Lauf verbringt rund 37,9 Sekunden in
`DeferredMainThreadWork`. Die Queue wird bereits beim Einreihen erfasst. Als
nächstes fehlt die fachliche Aufteilung, nicht noch eine zweite Queue.

- [ ] pro Action Producer, Mod-/Assembly-Owner, Enqueue-, Warte- und Laufzeit erfassen
- [ ] Abhängigkeiten und echte Main-Thread-Pflicht jeder teuren Action bestimmen
- [ ] Top-Actions und nicht zuordenbare globale Arbeit im Benchmark-Report ausgeben
- [ ] reine Datenarbeit vorbereiten lassen und Ergebnisse in Originalreihenfolge im Main Thread übernehmen
- [ ] Fehler kontrolliert auf den originalen sequenziellen Pfad zurückführen oder den Load eindeutig abbrechen
- [ ] deterministische Reihenfolge und identisches Ergebnis wiederholt prüfen

## Verbleibende Play-Data-Ownership

FixWorld besitzt bereits die Reihenfolge. In diesen Bereichen delegieren die
Stage-Adapter die eigentliche Arbeit noch weitgehend an RimWorld.

### Mod- und Assembly-Boot

- [ ] `LoadModContent()` in Assembly-Discovery, Assembly-Load und nur eingereihte Asset-Arbeit zerlegen
- [ ] `GetAllFilesForModPreserveOrder()` und Assembly-Discovery pro Mod erfassen
- [ ] `CreateModClasses()` vollständig übernehmen und Konstruktor- sowie Harmony-Zeiten messen
- [ ] Mod-Reihenfolge und Harmony-Erwartungen bei jedem Cutover unverändert erhalten

### XML und Definitionen

- [ ] XML-Lesen, Patch-Anwendung und Def-Import getrennt messen
- [ ] Cross-References, Reference-Resolution und beide Implied-Phasen getrennt analysieren
- [ ] vorhandene RimWorld-Parallelisierung im Def-Aufbau erfassen, bevor FixWorld Worker hinzufügt
- [ ] Reflection, statische Resolver und globale Registry-Mutationen als Main-Thread-Grenzen dokumentieren

### Finalisierung und Lifecycle

- [ ] statische Konstruktoren, Atlas-Build, Asset-Unload und erzwungene GC getrennt messen
- [ ] LongEvent-Thread, synchrone Events, Szenenwechsel und Exception-Lebenszyklus als Runtime-Vertrag erfassen
- [ ] `MainMenuReady` nach `Menü -> Spiel -> Menü` in einem realen Save-Lauf erneut auslösen und verifizieren
- [ ] RimWorld- und Harmony-Aufrufe weiter auf dünne Adapter in typisierte FixWorld-Arbeit reduzieren

Akzeptanz für jeden Stage-Cutover:

- [ ] Modliste und Reihenfolge bleiben identisch; Hauptmenü, Quarry-Save, UI, Telemetrie und Benchmark funktionieren ohne relevante Fehler

## Scheduling und Worker

- [ ] Parallelität, Ressourcenklasse und Worker-Anzahl pro Stage anhand von CPU, Speicher und Datenträger messen
- [ ] RAM-, VRAM-, Queue-, GC-, Renderpausen- und Wall-Time pro Stage erfassen
- [ ] RimWorlds Unity Job System mit einem isolierten `IJob`-/`NativeArray`-Prototyp prüfen
- [ ] danach entscheiden, welche Arbeit Unity Jobs, FixWorld-Worker oder der Main Thread ausführen

## Benchmark und Pilotbetrieb

- [ ] Preloader für Benchmarks explizit schaltbar machen, statt den Installationszustand zu erben
- [ ] PNG/JPG, DDS und DDS-Build mit kaltem/warmem OS-Cache sowie zwei, vier und acht Workern vergleichen
- [ ] Read-ahead auf NVMe und HDD mit abgestuften Budgets messen, Suchzeit und Durchsatz getrennt
- [ ] Mod-Dateien und Assemblies budgetiert mit DDS vorladen und gegen keinen Read-ahead samt RAM-/I/O-Spitzen messen

## Diagnose, Logging und Ingame-UI

Ziel: Die Runtime besitzt genau eine günstige Diagnosequelle. Loader und Mod
stellen diese Daten nur an ihren jeweiligen Grenzen bereit. Ein geöffnetes UI
darf weder neue Patches installieren noch erst dann Profiling aktivieren.

- [ ] einen unveränderlichen, versionierten Runtime-Snapshot aus bestehender Early-Timeline, Stage-Telemetrie, Deferred-Arbeit, Scheduler-, DDS- und Speicherdaten zusammensetzen
- [ ] Benchmark-JSON, kompaktes Log-Summary und UI aus diesem Snapshot speisen, statt drei Messpfade zu pflegen
- [ ] immer aktive günstige Zähler von einer explizit aktivierbaren Detailaufzeichnung trennen
- [ ] Detailereignisse in einem begrenzten Ringpuffer halten und wiederholte Probleme nach Owner, Pfad und Fingerprint aggregieren
- [ ] im Loader nur Boot-Meilensteine, Contract-Fehler und Fallbacks loggen
- [ ] Early-Timeline-Felder eindeutig benennen; früh beobachtete Mod-Assemblies sind keine aktive Modanzahl
- [ ] bei `MainMenuReady` genau ein kompaktes Runtime-Summary mit Stage-Hotpaths, Deferred-Hotpaths, DDS-Zustand und Worker-Auslastung schreiben
- [ ] fehlende Texturen und NPOT-Warnungen nach Mod und Pfad zusammenfassen; ohne belastbare Zuordnung keinen FixWorld-Fehler behaupten
- [ ] über die normale Mod einen `MainButtonDef` und ein skalierbares Diagnosefenster anbieten; Runtime und Shared bleiben frei von Verse-UI
- [ ] Ansichten für Startup/Stages, Deferred/Mods, DDS/Worker und aggregierte Probleme bereitstellen
- [ ] das Fenster höchstens alle 250 bis 500 ms und nur bei neuer Snapshot-Version aktualisieren
- [ ] einen typisierten Diagnose-Export aus RimWorld anbieten, der denselben Vertrag wie der Benchmark verwendet

Akzeptanz:

- [ ] letzter abgeschlossener Start bleibt bis zum nächsten Start im UI sichtbar
- [ ] geschlossenes UI und Standard-Logging erzeugen keinen Log-Spam und keinen messbaren Hotpath
- [ ] Diagnosefenster funktioniert im Hauptmenü und im Spiel, ohne den Loader- oder Profiling-Zustand zu verändern

## Ingame, später

- [ ] eingefrorenen komplexen Save zweimal messen und den dominanten Tick-Pfad bestimmen
- [ ] `TickManager`, `MapPreTick`, `MapPostTick`, Unity-Jobs, FixWorld-Worker und Main-Thread-Zeit trennen
- [ ] Background-Jobs anhand von TPS, Framezeit, CPU- und I/O-Druck drosseln
- [ ] RimThreadeds Muster nur auf nachgewiesene RimWorld-1.6-Hotpaths übertragen

### Pathfinding

- [ ] vorhandene RimWorld-1.6-Path-Jobs instrumentieren, nicht vorschnell ersetzen
- [ ] `PushRequest`, `FindPathNow`, Queue-Latenz, Requests pro Tick, Batchgröße und `MapGridRequest`-Wiederverwendung erfassen
- [ ] `PathFinderMapData`, Request-Kontext, Traversal-Kosten und Invalidierungen getrennt erfassen
- [ ] Reachability und `ReachabilityCache` getrennt vom PathFinder profilieren
- [ ] erst danach Path-Reuse und gestufte Path-Caches mit präziser Invalidierung testen
- [ ] Zeit, expandierte Nodes, Pfadlänge, Worst Case, Hit-Rate und Invalidierungen berichten

## Plattform, später

- [ ] GPU-Dekodierung, Mipmaps und Uploads erst nach sauberer CPU-Aufteilung bewerten
- [ ] Linux-Konverter und Plattform-Fallback bauen
