# Reproduzierbares Benchmark-Protokoll

## Ziel

Dasselbe Save soll unter kontrollierten Bedingungen mehrfach geladen werden.
Diagnose-Laeufe finden den Engpass. Separate, moeglichst uninstrumentierte
A/B-Laeufe pruefen spaeter den Gesamteffekt einer einzelnen Optimierung.

## Testartefakte

Vor dem ersten Lauf erfassen:

- Save-Datei und SHA-256
- `Assembly-CSharp.dll` SHA-256
- RimWorld-Laufzeitbuild und Unity-Version
- aktive DLCs
- vollstaendige Modliste in exakter Reihenfolge
- Version und Hash jeder fuer den Test relevanten Mod-DLL
- relevante Mod-Konfigurationen
- Betriebssystem, CPU, RAM, GPU und Treiberversion
- Energieprofil, Spielaufloesung, Fenster/Vollbild, VSync und FPS-Limit

Das Testsave kommt unter `benchmarks/saves/` und bleibt ignoriert. Fuer jeden
Lauf wird das unveraenderte Ausgangssave neu geladen; RimWorld darf es nicht als
neuen Ausgangspunkt ueberschreiben.

## Drei getrennte Laufarten

### Baseline

- erforderliche Save-Mods
- kein Dubs-Profiling aktiv
- noch kein eigener Performance-Patch
- eingebauter 30-Sekunden-Benchmark

### Diagnose

- identische Save-/Modbasis
- Harmony und Dubs aktiv
- nur die aktuell untersuchte Kategorie instrumentiert
- Ergebnisse dienen zur Attribution, nicht als unverzerrte Gesamtbaseline

### A/B-Validierung

- Dubs deaktiviert
- A: eigener Performance-Patch deaktiviert
- B: genau dieser Patch aktiviert
- ansonsten identische Modliste, Reihenfolge und Konfiguration

## Ablauf eines Runs

1. RimWorld neu starten.
2. Ausgangssave laden.
3. dieselbe Map, Kameraposition und Zoomstufe einstellen.
4. keine Auswahl, Menues, Alerts oder Dev-Overlays offen lassen.
5. einen festen Warm-up von 3.600 Simulationsticks ab Save-Tick abwarten.
6. Zielgeschwindigkeit setzen.
7. den 30-Sekunden-Benchmark starten und nicht interagieren.
8. Rohwerte und Screenshot des Ergebnisses sichern.
9. `Player.log` auf neue Exceptions oder Spam pruefen.
10. Spiel fuer den naechsten Run komplett neu starten.

Der tickbasierte Warm-up ist einem rein zeitbasierten Warm-up vorzuziehen, weil
alle Runs denselben Simulationszustand erreichen sollen. Bis ein kleiner Harness
das automatisiert, muessen Starttick und Abweichung im Run-Datensatz stehen.

## Geschwindigkeitsmatrix

Mindestens drei Wiederholungen pro Zelle, bei kleinen Effekten besser fuenf:

| Modus | Ziel | Zweck |
|---|---:|---|
| 1x | 60 TPS | normale Spielbarkeit, oft TPS-gedeckelt |
| 2x | 180 TPS | mittlere Last |
| 3x aktiv | 360 TPS | primaere CPU-Saettigungsprobe |

Der 3x-Inaktivitaetsboost auf 720 TPS muss verhindert oder als eigener Testfall
ausgewiesen werden. Am einfachsten bleibt mindestens ein kontrollierter
Spielerpawn wach und die Spielsituation in jedem Run identisch.

## Rohmetriken

Pflicht:

- reale Messdauer
- Frames
- Game Ticks
- FPS
- TPS
- Ticks pro Frame
- Zielgeschwindigkeit und tatsaechlicher TickRateMultiplier
- Run-Nummer und Start-/Endtick

Diagnoseerweiterung:

- Zeit pro Subsystem/Methodenast
- Calls pro Messfenster
- Mittelwert und Maximum, spaeter moeglichst Perzentile
- Hauptthread- und Unity-Job-Worker-Auslastung
- GC Collections je Generation
- Heap vor/nach dem Fenster und Allokationsrate, soweit belastbar messbar
- PathRequest-Queue/Batchgroesse oder andere hypothesenspezifische Zaehler

`1000 / TPS` wird nur als effektive Wall-Clock-Zeit pro fortgeschrittenem Tick
notiert. Wenn das Spiel sein Ziel-TPS erreicht, ist dies eine Obergrenze und
keine gemessene CPU-Zeit des Ticks.

## Vergleich und Entscheidung

- Primaer den Median der Wiederholungen vergleichen.
- Streuung als Minimum/Maximum und Median Absolute Deviation festhalten.
- Einen Effekt unterhalb der normalen Run-Streuung nicht als Verbesserung
  deklarieren.
- Bei Dubs zuerst Anteil, absolute Zeit und Call Count gemeinsam betrachten.
- A/B nur akzeptieren, wenn der uninstrumentierte Gesamtbenchmark besser wird.
- Jede neue Exception, Log-Spam oder Verhaltensabweichung macht den Lauf fuer
  einen Erfolg unbrauchbar.

## Korrektheitspruefung nach einem Patch

Mindestens:

- Save laden und erneut speichern/laden
- Pawn-Jobs und Reservations
- Pfadsuche und Reachability nach Tueren, Bauten und Zerstoerung
- Kampf, Spawn und Tod
- relevante DLC-Mechaniken
- mehrere Ingame-Tage unbeaufsichtigter Lauf
- keine neuen Exceptions oder wiederholten Warnungen

Je nach Patch kommen spezifische Invarianten und Zustandsvergleiche hinzu.
