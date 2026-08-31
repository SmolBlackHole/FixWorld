# TODO

Aktuell: **DDS-Erzeugung als nächsten echten `ParallelThenCommit`-Slice zerlegen**

## Erledigter Stand

- [x] FixWorld, Build, Profiler und typisierten Loader-Benchmark aufsetzen
- [x] Start in `Bootstrap`, `XML`, `Definitions`, `Content` und `Finalize` gliedern
- [x] eigene Ladeanzeige aus demselben Loader-Zustand zeichnen
- [x] `ExecuteToExecuteWhenFinished()` während des Starts übernehmen
- [x] 13.707 verzögerte Aufgaben in Vanilla-Reihenfolge framefähig ausführen
- [x] UI zwischen Aufgaben mindestens etwa alle 100 bis 150 ms rendern lassen
- [x] vollständige Liste mit 88 aktiven Mods bis zum Hauptmenü testen
- [x] Quarry innerhalb des FixWorld-Runners erfolgreich initialisieren
- [x] warmen DDS-Start mit Runner messen: 24,8 bis 27,6 Sekunden
- [x] automatischen, invalidierbaren DDS-Cache für geeignete Mod-Texturen bauen
- [x] 10.460 warme DDS-Treffer ohne Cache-Miss laden
- [x] `texconv` für Windows x64 mit Lizenz bündeln

## Erledigt: Content-Pipeline

- [x] verzögerte Aufgaben nach Methode, Mod, Aufrufen und exklusiver Zeit auswerten
- [x] `ModContentPack.ReloadContentInt` und vorhandene Harmony-Patches als Vertrag prüfen
- [x] eine `ContentLoadingPipeline` in ursprünglicher Mod-Reihenfolge bauen
- [x] pro Mod `Audio -> Textures -> Strings -> Asset bundles` als echte Unterstufen ausführen
- [x] Fortschritt und aktuelle Mod direkt melden, nicht aus `DeepProfiler` erraten
- [x] spätestens vor bekannten langen Unterstufen einen Frame ermöglichen
- [x] Unity-Objekte und Uploads weiterhin ausschließlich auf dem Hauptthread ausführen
- [x] vollständige Modliste, Quarry und warmen DDS-Cache erneut testen
- [x] Laufzeit gegen den aktuellen Runner-Baselinebereich von 24,8 bis 27,6 Sekunden vergleichen

## Erledigt: Loader-Grundlage

- [x] 589 statische Konstruktoren einzeln messen und als Enumerator ausführen
- [x] Lunars `CallAll`-Postfix als eigene Unterstufe messen: 3.529 bis 3.584 ms, davon 676 bis 768 ms Harmony-Wrapper und etwa 2.816 bis 2.853 ms Framework-/Komponenteninitialisierung
- [x] Content- und Finalize-Pipeline auf einen gemeinsamen Stage- und Work-Item-Vertrag umstellen
- [x] Stage-Abhängigkeiten als validierten Graphen mit deterministischen Barrieren ausführen
- [x] `MainThread`, `Ordered`, `Parallel` und `ParallelThenCommit` als explizite Ausführungsmodi modellieren
- [x] Work-Items explizit als `MainThread` oder `WorkerSafe` klassifizieren
- [x] gemeinsamen Scheduler nach Zeitbudget rendern lassen, ohne Unity-Arbeit auf Worker zu verschieben
- [x] Ausführungs-, Hauptthread-, Worker-, Warte- und Wall-Time pro Work-Item erfassen
- [x] Mod-Zuordnung als `Exact`, `Inferred` oder `Global` mit Zeiten pro Operation berichten
- [x] FixWorld-Eigenaufwand für Klassifizierung, Scheduling und Telemetrie separat messen
- [x] vollständige 88-Mod-Liste mit Stage-Graph und Quarry erneut fehlerfrei testen
- [x] Stage-, Work-Item-, Profiler- und Cache-Zustände über eine gemeinsame Mailbox an UI und Telemetrie verteilen
- [x] häufige UI-Zwischenstände zusammenfassen und Start-/Abschluss-/Fehlerereignisse verlustfrei erhalten

## Nächster Slice: erste Worker-Arbeit

- [x] Worker-Ausführung für `Parallel` und `ParallelThenCommit` im gemeinsamen Scheduler bereitstellen
- [x] Harmony für den unterstützten Dateiladevertrag nur als Einstieg verwenden und Discovery, DDS-Validierung sowie Commit vollständig über FixWorld ausführen
- [ ] bei fremden inkompatiblen Patches oder einem nicht unterstützten Vertrag kontrolliert RimWorlds Originalpfad verwenden
- [x] DDS-Validierung pro Mod in unveränderliche Worker-Eingaben und geordnete Main-Thread-Commits teilen
- [x] DDS-Validierung in Mod-Batches ausführen, nicht als Task pro Textur
- [x] sequenziellen Fallback und expliziten A/B-Schalter für den DDS-Worker-Pfad bereitstellen
- [x] Workeranzahl und aktiven DDS-Ausführungsmodus im Benchmark berichten
- [x] identische Dateizuordnung, identischen Cache-Index, 10.460 Treffer und keine unerwarteten Misses prüfen
- [x] zwei sequenzielle und zwei parallele Läufe mit vollständiger Modliste vergleichen
- [x] DDS aus, kalten Aufbau und Warmstart vergleichen: 47,2 s, 124,5 s und 25,6 s Loaderzeit
- [x] Workerstandard aus der Hälfte der logischen CPUs ableiten und per Umgebung überschreibbar lassen
- [ ] Parallelität je Stage bestimmen: Validierung war mit 4 Workern schneller als mit 8
- [ ] `texconv` und reine DDS-Erzeugung parallel ausführen, Ergebnisse atomar und geordnet veröffentlichen
- [ ] DDS-Build mit 2, 4 und 8 Workern vergleichen
- [ ] Cache-Index früh auf einem Worker laden und vor der ersten DDS-Abfrage geordnet übernehmen
- [ ] Texturvorbereitung von Unity-Erzeugung, `Apply`, Kompression und Upload trennen
- [ ] Renderpausen und reine Wall-Time pro framefähiger Stage getrennt berichten
- [ ] `ThingDef.PostLoad`, Sound-Auflösung und Atlas-Build getrennt messen
- [ ] XML-Patches, Def-Auflösung, Reflection und Harmony-Scanning einzeln bewerten
- [ ] statische Konstruktoren weiterhin geordnet übernehmen und nur nach mod-spezifischem Nachweis optimieren
- [ ] Discovery, Read-ahead, Cache-Validierung und reine Byte-Verarbeitung als erste Worker-Kandidaten messen
- [ ] begrenzten Worker-Pool mit Parallelitäts-, Byte- und Queue-Limit sowie Backpressure bauen
- [ ] Worker-Ergebnisse geordnet an den Hauptthread übergeben und Unity-Objekte nur dort erzeugen oder verändern
- [ ] Workerfehler abbrechen oder kontrolliert auf den sequenziellen Originalpfad zurückführen
- [ ] deterministische Ergebnis- und Commit-Reihenfolge über wiederholte Läufe prüfen
- [ ] Worker-Anzahl gegen CPU-Kerne, Speicherdruck sowie NVMe, SATA und HDD benchmarken
- [ ] RAM-, VRAM-, Queue- und GC-Spitzen pro Stage erfassen

## Benchmark

- [x] normales Mod-Loading mit und ohne DDS reproduzierbar messen
- [x] vollständige aktive Modliste über `--live-mods` testen
- [ ] Preloader-Modus im Report als `on` oder `off` speichern
- [ ] Doorstop-Version und Zeit vom frühen Einstieg bis zum normalen FixWorld-Entrypoint berichten
- [ ] Preloader für Benchmarks explizit schaltbar machen, statt den Installationszustand zu erben
- [ ] Preloader `off` und `on` erst vergleichen, sobald der frühe Einstieg echte Arbeit übernimmt
- [ ] PNG/JPG sowie DDS jeweils mit kaltem und warmem OS-Dateicache messen
- [ ] NVMe, SATA-SSD und HDD als getrennte Hardwareprofile behandeln
- [ ] Pilotlauf auf der HDD und großen Modliste des Testnutzers durchführen

## DDS-Cache

- [x] Quellpfad, Größe, Änderungszeit und Cacheformat zur Invalidierung verwenden
- [x] Cache-Dateien erst nach erfolgreicher Konvertierung atomar veröffentlichen
- [x] umgedrehte DDS-Texturen durch das korrigierte Cacheformat invalidieren
- [x] maximale Cachegröße standardmäßig auf 6 GiB begrenzen und per Einstellung konfigurierbar machen
- [x] JSON-Index atomar schreiben und nach fehlendem oder beschädigtem Index sicher rekonstruieren
- [x] entfernte Quellen, deaktivierte Mods und die am längsten ungenutzten Einträge bereinigen
- [x] Cache-Schlüssel um Quellinhalt, Cacheformat, Zielformat und Konverter-Identität erweitern
- [ ] Plattform-Backend in die Identität aufnehmen, sobald neben Windows ein zweites Backend existiert
- [ ] erstmaligen DDS-Build beschleunigen, ohne den normalen Start zu blockieren
- [ ] BC3-DDS gegen unkomprimierte DDS vergleichen
- [ ] PNG/JPG begrenzt parallel dekodieren und nur fertige Daten geordnet übernehmen

## Optionaler Preloader

- [x] Doorstop nur nach Opt-in installieren
- [x] fremde Proxy-DLLs und Konfigurationen niemals überschreiben
- [x] Installation, Deaktivierung und Entfernung über das mitgelieferte Tool unterstützen
- [x] Preloader und normalen FixWorld-Mod voneinander entkoppeln
- [ ] frühen Hookpunkt belegen, bevor dort RimWorld- oder Harmony-Arbeit übernommen wird
- [ ] vollständiges Assembly-Laden erst danach messen

Der Preloader ist für den aktuellen Staged Loader nicht erforderlich und bleibt vorerst optional.

## Ingame

- [ ] eingefrorenen komplexen Save zweimal messen
- [ ] mit Dubs den dominanten Tick-Pfad bestimmen
- [ ] genau eine Optimierung implementieren und per A/B-Test bewerten

### Pathfinding-Slice (später)

- [ ] Path-Requests nach Pawn, Ziel, Traversal-Modus und relevanten Kostenprofilen erfassen
- [ ] Kosten und Abhängigkeiten für Türen, Gefahren, Feuer, Reservierungen, Terrain, Gebäude, temporäre Hindernisse, Regionen und Zonen untersuchen
- [ ] Path-Reuse und gestufte Path-Caches für identische oder hinreichend ähnliche Requests prüfen
- [ ] präzise Invalidierung bei Änderungen an Türen, Feuer, Reservierungen, Terrain, Gebäuden, Hindernissen, Regionen und Zonen modellieren
- [ ] Verhalten und Contention bei vielen gleichzeitig pfadsuchenden Pawns messen
- [ ] Zeit pro Path-Request, Requests pro Tick, expandierte Nodes, Pfadlänge, Worst-Case-Nodes, Cache-Hit-Rate und Invalidierungen berichten
- [ ] Path-Reuse als erste Hypothese per A/B-Test gegen unverändertes RimWorld prüfen

## Später

- [ ] parallele Discovery und Read-ahead auf HDD/SATA testen
- [ ] DDS-Pack erst nach einer direkten Byte-/Stream-Ladegrenze erneut bewerten
- [ ] OBST als mögliches Packformat mit Sidecar-Index prüfen
- [ ] GPU-Dekodierung, Mipmaps und Uploads erst nach sauberer CPU-Aufteilung bewerten
- [ ] Linux-Konverter und Plattform-Fallback bauen
