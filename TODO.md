# TODO

Aktuell: **Staged Loader: nächsten messbaren Engpass auswählen**

## Einmalig

- [x] Repo vereinfachen
- [x] `spoon-spring-v1` einfrieren
- [x] Dubs Performance Analyzer installieren
- [ ] Dubs beim nächsten Spielstart aktivieren
- [x] bereinigten Stand committen
- [x] Mod und Package-ID vollständig auf FixWorld umstellen

## Mod-Loading

- [x] zwei unveränderte Starts messen
- [x] Texturpfad messen: 1,44 s Lesen, 20,19 s Verarbeitung
- [x] Hauptthread messen: `LoadImage` 16,35 s, `Apply` 2,60 s, `FastCompressDXT` 0,48 s
- [x] reine `FastCompressDXT`-Mikrobatches verwerfen: der Anteil ist zu klein
- [x] Texturkompression ausschalten: nur etwa 0,76 s im Texturpfad gespart
- [x] Read-ahead zurückbauen: nur etwa 1,3 s Datei-I/O gegenüber 16 s `LoadImage`
- [x] DDS-PoC nur für Vanilla Textures Expanded und Clean Textures: Texturpfad 20,71 s auf 13,85 s, rund 33 % schneller
- [x] Vollcache messen: 10.461 erzeugte DDS, 162 ungeeignete Abmessungen, 4 Hospitality-PSD, 0 Fehler
- [x] automatischen, invalidierbaren DDS-Cache für alle geeigneten aktiven Mod-Texturen bauen
- [x] DDS A/B testen: Texturpfad 21,16 s ohne Cache gegenüber 2,24 bis 2,51 s mit warmem Vollcache
- [x] `texconv` für Windows x64 inklusive Lizenz und Prüfsumme bündeln
- [x] Cache ohne künstliches Standardlimit aufbauen und mindestens 10 GiB freien Plattenplatz reservieren
- [x] Budget-Fallback testen: 19 DDS bis exakt 1 MiB, 10.442 übersprungen, Spielstart gültig

## Staged Loader (aktuell)

- [x] normalen Mod und optionalen frühen Doorstop-Bootstrap klar trennen
- [x] Doorstop nur nach Opt-in installieren und fremde Proxy-Dateien niemals überschreiben
- [x] Doorstop von RimWorld und Harmony entkoppeln und die Installationslogik zwischen Mod und CLI teilen
- [x] normale FixWorld-Patches beim Laden von `FixWorld.dll` zentral über Harmony registrieren
- [ ] vollständiges Assembly-Laden später über den optionalen Preloader messen
- [x] reinen Doorstop-Prozesseinstieg bis zum Hauptmenü verifizieren
- [ ] korrekten frühen RimWorld-Hookpunkt belegen, bevor dort Harmony-Patches registriert werden
- [ ] Absturz der vollständigen isolierten Mod-Fixture bei Quarry unabhängig von Preloader und FixWorld-Runner klären
- [x] Vanilla-Start in `Bootstrap -> XML & Patches -> Definitions -> Content -> Finalize` zerlegen
- [x] zentralen Loader-Observer statt einer großen Methoden-Patchliste bauen
- [x] eigene FixWorld-Ladeanzeige aus demselben Loader-Snapshot zeichnen
- [x] FixWorld-Anzeige bei verzögerten Initialisierungsaufgaben und statischen Konstruktoren testen
- [x] typisierten C#-Report und kleines Python-Benchmarktool bauen
- [ ] `LoadTextures` und statische Konstruktoren als nächste Engpässe getrennt untersuchen
- [x] parallele Discovery auf NVMe vorerst zurückstellen: nur 74,8 ms Gesamtpotenzial
- [ ] Überschreibungen vor Dekodierung auflösen, damit Verlierer gar nicht vorbereitet werden
- [x] Cache-Artefakte inkrementell anhand Quellpfad, Größe, Änderungszeit und Cacheformat aktualisieren
- [ ] Cache-Schlüssel aus Quellinhalt, Loader-Version, Plattform und Zielformat bilden
- [ ] PNG-Dekodierung, Mipmaps und BC3-Kompression in begrenzten Worker-Batches vorbereiten
- [x] Cache-Ergebnisse erst nach erfolgreicher Konvertierung atomar veröffentlichen
- [ ] Hauptthread nur fertige DDS-/Raw-Artefakte in ursprünglicher Mod-Reihenfolge übernehmen lassen
- [ ] Worker-Pipeline mit fester Obergrenze und Backpressure statt unbegrenzter Parallelität bauen
- [ ] RAM- und VRAM-Budget in Bytes statt nur eine feste Anzahl Dateien begrenzen
- [ ] Puffer wiederverwenden und Allokationen sowie GC-Pausen pro Stage messen
- [ ] heiße Texturen vorladen und seltene Texturen optional lazy laden
- [ ] Zeit, Trefferquote, Queue-Stalls, RAM, VRAM und Cachegröße pro Stage messen

## DDS-Messmatrix

- [ ] PNG/JPG ohne DDS-Cache mit kaltem OS-Dateicache messen
- [ ] PNG/JPG ohne DDS-Cache mit warmem OS-Dateicache messen
- [x] erstmaligen DDS-Build getrennt messen: 10.461 DDS in 91,0 s, Gesamtstart 143,1 s
- [ ] vorhandenen DDS-Cache mit kaltem OS-Dateicache messen
- [x] vorhandenen DDS-Cache mit warmem OS-Dateicache messen: 34,4 s und 38,8 s Gesamtstart
- [x] nach dem DDS-Build keinen vermeintlich kalten Start messen: der Generator wärmt bereits den OS-Cache
- [x] bisherige A/B-Paare mit identischer 89-Mod-Fixture und explizit aktiviertem/deaktiviertem Anwendungscache messen
- [ ] NVMe, SATA-SSD und HDD als getrennte Hardwareprofile behandeln

## Weitere Loader-Experimente

- [x] DDS-Pack/Seeking auf NVMe zurückstellen: der Vollcache lädt warm 1,77 GB in 1,94 bis 2,18 s
- [x] Duplikate zählen: 60 Pfade, 66 überschattete Dateien, knapp 5 MB
- [x] Skip überschatteter Texturen verwerfen: kleiner Gewinn, aber Risiko für `GetAllInFolder`
- [ ] BC3-DDS gegen unkomprimierte DDS vergleichen: CPU-Zeit, Dateigröße und Ladezeit
- [ ] PNG/JPG per Worker dekodieren und nur `Texture2D`/Upload auf dem Hauptthread ausführen
- [ ] begrenzte Dekodierpipeline mit Batchgrößen 4, 16 und 64 vergleichen
- [x] Dateikatalog auf NVMe messen: 358 Scans und 11.560 Dateien brauchen nur 74,8 ms
- [ ] Dateikatalog und parallele Discovery auf HDD/SATA mit kaltem Dateisystem-Cache messen
- [ ] Lazy Loading für selten oder nie verwendete Texturen untersuchen
- [ ] statische Konstruktoren und langsame Mod-Konstruktoren einzeln messen
- [ ] `ThingDef.PostLoad`, Sound-Auflösung und XML-Patches getrennt messen
- [ ] Assembly-Scanning, Reflection und Harmony-Patching auf wiederholbare Arbeit prüfen

## GPU-Texturpipeline

- [ ] `LoadImage` in Dekodierung, Mipmaps, Kompression und GPU-Upload zerlegen, bevor wir Hardware auswählen
- [ ] CPU-Worker-Dekodierung plus gebündelten GPU-Upload als kleinsten staged PoC testen
- [ ] Mipmap-Erzeugung auf der GPU gegen CPU/Unity vergleichen
- [x] Vanilla prüfen: `FastCompressDXT` nutzt auf unterstützten GPUs bereits den Compute Shader `EncodeBCn`
- [ ] prüfen, ob sich das vorhandene GPU-BC3-Ergebnis ohne teuren Readback als Cache-Artefakt exportieren lässt
- [ ] GPU-Ergebnisse direkt resident halten und teuren Readback zur CPU vermeiden
- [ ] Uploads nach Byte-Budget batchen und VRAM-Spitzen messen
- [ ] Feature-Erkennung und sauberen CPU-Fallback für ungeeignete GPUs vorsehen
- [ ] GPU-Pfad auf integrierter GPU, Mittelklasse und viel VRAM getrennt bewerten
- [ ] DirectStorage oder GPU-native Container erst prüfen, wenn DDS-I/O auf HDD/kaltem Cache wirklich dominiert

## Fenster und Monitor

- [x] Benchmark und lokalen Launcher standardmäßig maximiert auf dem G276HL starten
- [x] G276HL anhand Hardware-ID/Friendly Name statt nur anhand des Monitorindex finden
- [x] Unity-Monitor per Startargument setzen, gespeicherte Registry-Werte unberührt lassen
- [x] Fenster erst verschieben, dann maximieren und tatsächlichen Monitor über `MonitorFromWindow` verifizieren
- [x] Fallback für ausgeschalteten oder getrennten Zielmonitor testen
- [x] Vollbild-, Borderless- und Fenstermodus nicht dauerhaft überschreiben
- [ ] später als Mod-Einstellung produktisieren, falls auch normale Steam-Starts korrigiert werden sollen

## Ingame-TPS

- [ ] Save zweimal mit dem eingebauten Benchmark messen
- [ ] mit Dubs den dominanten Tick-Pfad finden
- [ ] genau eine Optimierung testen
- [ ] A/B vergleichen, behalten oder zurückbauen

## Später

- [ ] parallele Discovery, Read-ahead und Look-ahead auf HDD/SATA gesondert testen
- [ ] DDS-Pack erst nach einer direkten Byte-/Stream-Ladegrenze erneut bewerten
- [ ] OBST als konformes Packformat mit rebuildbarem Sidecar-Index prüfen
- [ ] pro Mod eine `.fwp`-Datendatei und einen atomaren `.fwi`-Index bauen
- [ ] geänderte Texturen nur anhängen, entfernte Einträge aus dem Index löschen
- [ ] Packdatei ab 25 % ungenutzten Daten budgetabhängig kompaktieren
- [ ] LRU-Bereinigung innerhalb des Cache-Budgets untersuchen
- [ ] Linux-Konverter bauen und Originaltextur-Fallback beibehalten
