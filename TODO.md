# TODO

Aktuell: **Die übernommene Startpipeline in messbare Teilpipelines zerlegen**

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

## Nächster Slice: Content-Pipeline

- [ ] verzögerte Aufgaben nach Methode, Mod, Aufrufen und exklusiver Zeit auswerten
- [ ] `ModContentPack.ReloadContentInt` und vorhandene Harmony-Patches als Vertrag prüfen
- [ ] eine `ContentLoadingPipeline` in ursprünglicher Mod-Reihenfolge bauen
- [ ] pro Mod `Audio -> Textures -> Strings -> Asset bundles` als echte Unterstufen ausführen
- [ ] Fortschritt und aktuelle Mod direkt melden, nicht aus `DeepProfiler` erraten
- [ ] spätestens vor bekannten langen Unterstufen einen Frame ermöglichen
- [ ] Unity-Objekte und Uploads weiterhin ausschließlich auf dem Hauptthread ausführen
- [ ] vollständige Modliste, Quarry und warmen DDS-Cache erneut testen
- [ ] Laufzeit gegen den aktuellen Runner-Baselinebereich von 24,8 bis 27,6 Sekunden vergleichen

## Danach: weitere blockierende Stufen

- [ ] statische Konstruktoren einzeln messen und als Enumerator ausführen
- [ ] `ThingDef.PostLoad`, Sound-Auflösung und Atlas-Build getrennt messen
- [ ] XML-Patches, Def-Auflösung, Reflection und Harmony-Scanning einzeln bewerten
- [ ] nur nach einem reproduzierbaren Engpass sichere I/O-Arbeit in Worker-Batches verschieben
- [ ] Worker-Pipeline mit Byte-Limit und Backpressure statt unbegrenzter Parallelität bauen
- [ ] RAM-, VRAM-, Queue- und GC-Spitzen pro Stage erfassen

## Benchmark

- [x] normales Mod-Loading mit und ohne DDS reproduzierbar messen
- [x] vollständige aktive Modliste über `--live-mods` testen
- [ ] Preloader-Modus im Report als `on` oder `off` speichern
- [ ] Preloader für Benchmarks explizit schaltbar machen, statt den Installationszustand zu erben
- [ ] Preloader `off` und `on` erst vergleichen, sobald der frühe Einstieg echte Arbeit übernimmt
- [ ] PNG/JPG sowie DDS jeweils mit kaltem und warmem OS-Dateicache messen
- [ ] NVMe, SATA-SSD und HDD als getrennte Hardwareprofile behandeln
- [ ] Pilotlauf auf der HDD und großen Modliste des Testnutzers durchführen

## DDS-Cache

- [x] Quellpfad, Größe, Änderungszeit und Cacheformat zur Invalidierung verwenden
- [x] Cache-Dateien erst nach erfolgreicher Konvertierung atomar veröffentlichen
- [x] umgedrehte DDS-Texturen durch das korrigierte Cacheformat invalidieren
- [ ] maximale Cachegröße konfigurierbar begrenzen und alte Einträge bereinigen
- [ ] Cache-Schlüssel um Quellinhalt, Loader-Version, Plattform und Zielformat erweitern
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

## Später

- [ ] parallele Discovery und Read-ahead auf HDD/SATA testen
- [ ] DDS-Pack erst nach einer direkten Byte-/Stream-Ladegrenze erneut bewerten
- [ ] OBST als mögliches Packformat mit Sidecar-Index prüfen
- [ ] GPU-Dekodierung, Mipmaps und Uploads erst nach sauberer CPU-Aufteilung bewerten
- [ ] Linux-Konverter und Plattform-Fallback bauen
