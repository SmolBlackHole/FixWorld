# TODO

Aktuell: **Den frühen Doorstop-Einstieg absichern und den Mod-Boot schrittweise als eigene FixWorld-Pipeline übernehmen.**

Der abgeschlossene Stand gehört in Git, Benchmarks und Logs. Diese Datei enthält nur noch bewusst verschobene Arbeit.

## Nächster Schnitt: früher Mod-Boot

- [ ] `LoadedModManager.LoadAllActiveMods()` am Doorstop-Einstieg zunächst ausschließlich beobachten und Vanilla vollständig weiterlaufen lassen
- [ ] Mod-Metadaten, Assembly-Discovery, Assembly-Loading, Mod-Konstruktoren und Harmony-Zeiten typisiert erfassen
- [ ] Ursache des langen frühen `...`-Abschnitts mit dieser Telemetrie belegen
- [ ] Hook-Vertrag gegen RimWorld-Version, Assembly-MVID, Methodensignatur und Harmony-Owner absichern
- [ ] `GetAllFilesForModPreserveOrder()` und Assembly-Discovery beobachten oder indexieren, noch nicht ersetzen
- [ ] LongEvent-Thread, synchrone Events, Szenenwechsel und Exception-Lebenszyklus als Vertrag erfassen
- [ ] erst danach `LoadAllActiveMods()` schrittweise über einen FixWorld-Coordinator übernehmen
- [ ] Assembly- und Harmony-Reihenfolge unverändert delegieren, bis Messungen eine sichere Übernahme begründen

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
