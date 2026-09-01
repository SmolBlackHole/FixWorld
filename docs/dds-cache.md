# DDS-Cache

## Ziel

FixWorld überspringt bei geeigneten Mod-Texturen die wiederholte PNG-/JPG-Dekodierung,
Mipmap-Erzeugung und BC3-Kompression. Der Cache darf weder Spielinhalte verändern noch
unkontrolliert Speicherplatz belegen.

## Gemessener Stand

- 10.460 wiederverwendete DDS bei 88 aktiven Mods
- Texturpfad ohne Cache: 21,16 s
- Texturpfad mit warmem Vollcache: 2,24 bis 2,51 s
- warmer Gesamtstart mit Stage-Runner und JSON-Index: 27,5 s im aktuellen Referenzlauf
- Vollcache: rund 1,59 GiB

Der erstmalige Cache-Build ist mit rund 91 s bewusst teurer. Der Gewinn entsteht bei
allen folgenden Starts.

## Gültigkeit und Plattenbudget

`index.json` ordnet jeder Quelle ihre DDS-Datei, Dateigröße, Änderungszeit,
Inhalts-Hash, Konverter-Identität und letzte Verwendung zu. Größe und Änderungszeit
bilden den schnellen Startpfad. Ändert sich eine dieser Angaben, vergleicht FixWorld
den SHA-256-Inhalts-Hash und konvertiert nur bei einer tatsächlichen Änderung neu.

Der Index wird über eine temporäre Datei mit Flush und atomarem Austausch geschrieben.
Der vorherige Stand bleibt als `index.backup.json` erhalten. Ein fehlender oder
beschädigter Index wird aus vorhandenen DDS-Dateien rekonstruiert, statt den Cache
pauschal zu löschen.

PNG- und JPG-Quellen werden beim DirectXTex-Export vertikal gespiegelt, damit RimWorlds
direkter DDS-Raw-Load dieselbe Ausrichtung wie Unitys normaler Bildpfad erhält. Die
Cacheformat-Version invalidiert ältere, falsch ausgerichtete Einträge automatisch.

- Standardlimit: 6 GiB, in den FixWorld-Einstellungen zwischen 1 und 64 GiB wählbar
- mindestens verbleibender freier Plattenplatz: 10 GiB
- optionaler Override über `FIXWORLD_DDS_CACHE_MAX_GIB`
- Cacheeintrag erst nach erfolgreicher Konvertierung atomar bereitstellen
- bei fehlendem Konverter oder ausgeschöpftem Plattenbudget wird die Originaltextur geladen
- entfernte Texturen und deaktivierte Mods werden bereinigt
- bei Überschreitung des Limits werden die am längsten ungenutzten Einträge zuerst entfernt

Index laden, Cache prüfen, DDS erzeugen, veraltete Einträge entfernen und Index speichern
werden über die Stage-Mailbox veröffentlicht. UI und Benchmark sehen damit denselben
typisierten Zustand, ohne den DDS-Code direkt zu kennen.

Der Cache lässt sich ohne Python aus dem FixWorld-Modordner prüfen oder entfernen:

```powershell
.\Tools\Windows-x64\FixWorld.Tool.exe dds-cache status
.\Tools\Windows-x64\FixWorld.Tool.exe dds-cache clean
```

Der erste Aufruf ist nur ein Dry-Run. `clean` löscht ausschließlich erkannte
FixWorld-DDS- und Staging-Dateien und verweigert die Ausführung, solange RimWorld läuft.

## Geplantes Packformat

Jeder Mod erhält eine große `.fwp`-Datendatei und einen kleinen `.fwi`-Index. Der Index
ordnet den Quelltexturen Offset und Länge ihrer DDS-Daten zu.

- unveränderte Einträge bleiben im Pack
- neue oder geänderte DDS werden angehängt
- entfernte Texturen verschwinden aus dem nächsten Index
- der Index wird über eine temporäre Datei atomar ersetzt
- alte Daten werden ab 25 % Verschnitt kompaktiert, sofern das Plattenbudget reicht

Der separate Index verhindert, dass bei jeder kleinen Mod-Aktualisierung die gesamte
Packdatei neu geschrieben werden muss.

## Plattformen

Das Packformat und der Cache-Leser sollen plattformneutral bleiben. Nur die Erzeugung
der DDS benötigt ein plattformspezifisches Backend.

- Windows: gebündeltes `texconv.exe` aus DirectXTex unter `Tools/Windows-x64/`
- Linux: eigener kleiner DirectXTex-Wrapper oder CompressonatorCLI
- ohne passendes Backend: bestehende Cacheeinträge lesen, Cache-Misses unverändert laden

Eine Windows-EXE ist keine Linux-Lösung. AMD und NVIDIA sind dagegen keine Trennlinie:
BC3/DXT5 ist das gespeicherte Texturformat und nicht an einen der beiden Hersteller
gebunden.

DirectXTex selbst lässt sich unter Linux bauen und kann PNG/JPEG über libpng und
libjpeg verarbeiten. Das offizielle `texconv`-Programm wird im DirectXTex-CMake-Projekt
jedoch nur für Windows erzeugt. Für FixWorld sind deshalb zwei Linux-Kandidaten offen:

1. ein kleiner eigener CLI-Wrapper um DirectXTex, libpng und libjpeg
2. AMD CompressonatorCLI, das Windows und Linux unterstützt

Vor dem Pilot-Test vergleichen wir Ausgabe, Laufzeit, Paketgröße und Lizenzaufwand.
Der eigene Wrapper ist voraussichtlich kleiner und hält Windows und Linux näher
beieinander; Compressonator ist die fertige Referenzlösung.

Quellen: [DirectXTex-CMake](https://github.com/microsoft/DirectXTex/blob/main/CMakeLists.txt),
[DirectXTex PNG/JPEG](https://github.com/microsoft/DirectXTex/wiki/Using-JPEG-PNG-OSS),
[Compressonator](https://github.com/GPUOpen-Tools/compressonator)

Der aktuelle Windows-Build ist DirectXTex `2026.5.8.1`, 966.480 Bytes,
SHA-256 `DCFDEC10244E02CF5037FBA089C55FB7E1326B1C8181742D77D15FA5CB5EEF06`.
Die zugehörige MIT-Lizenz liegt direkt im `Tools`-Ordner.

## Messregeln

Cold Start, warmer Anwendungscache und warmer Betriebssystem-Dateicache sind getrennte
Zustände. A/B-Läufe verwenden dieselbe Modliste, dieselbe Cachevariante und denselben
Ausgangszustand. Ein Cache-Build zählt nicht als kalter Folgelauf, weil er den
Betriebssystem-Cache bereits erwärmt.
