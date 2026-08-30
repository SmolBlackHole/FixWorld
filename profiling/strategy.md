# Profiling-Strategie

## Rollen der Werkzeuge

Kein einzelnes Werkzeug deckt Baseline, Ursachenanalyse, Allokationen und
Regressionen zugleich ab. Der sinnvolle Ablauf ist deshalb gestuft.

### 1. Eingebauter RimWorld-Benchmark

Nutzen: niedrig verzerrte End-to-End-Baseline und spaeterer A/B-Vergleich.

RimWorld 1.6 enthaelt im Dev-Menue die Aktion `Benchmark performance`. Sie misst
fest 30 Sekunden und zeigt Rohwerte fuer verstrichene Zeit, Frames und Game
Ticks sowie abgeleitete FPS, TPS und Ticks pro Frame.

Staerken:

- bereits im exakten Spielbuild vorhanden
- kein zusaetzlicher Patch waehrend der Messung
- geeignet, um festzustellen, ob eine Optimierung das Gesamtsystem wirklich
  verbessert

Grenzen:

- nur Durchschnittswerte fuer das Gesamtspiel
- keine Subsystemzeit, Call Counts, Allokationsrate oder Perzentile
- bei erreichtem Ziel-TPS ist `1000 / TPS` kein Mass fuer die echte Tickkosten

### 2. Dubs Performance Analyzer

Nutzen: beste erste Ursachenanalyse innerhalb RimWorlds.

Der aktuelle Quellstand hat ein eigenes 1.6-Artefakt. Dubs kann Tick- und
Update-Kategorien, Methoden/Typen, Harmony-Patches und interne Aufrufe messen.
Es zeigt Call Counts sowie Durchschnitts- und Maximalwerte und kann gezielt einen
Methodenpfad weiter aufteilen.

Vorgehen:

1. zuerst grobe Kategorien messen
2. nur den dominanten Ast weiter aufteilen
3. Stacktraces und Internal Profiling nur kurz und gezielt aktivieren
4. Diagnosewerte nie direkt mit einem uninstrumentierten A/B-Benchmark
   vergleichen

Grenzen:

- Harmony- und Stopwatch-Instrumentierung erzeugt Overhead
- massenhaft gepatchte Kleinstmethoden koennen das Ergebnis stark verzerren
- Stacktraces sind besonders teuer
- bei Exceptions koennen Timerwerte laut Projektdokumentation falsch sein

Quelle: [Dubs Performance Analyzer](https://github.com/Dubwise56/Dubs-Performance-Analyzer).

### 3. Eigene gezielte Harmony-Instrumentierung

Nutzen: genaue Hypothesenpruefung, nachdem Dubs einen konkreten Pfad eingegrenzt
hat.

Ein Prefix kann Startzeit und Zaehler aufnehmen, Postfix plus Finalizer koennen
den Abschluss auch fuer Ausnahmefaelle korrekt behandeln. Aggregiert wird im
Speicher; Ausgabe erfolgt nur am Ende eines Messfensters. Damit vermeiden wir
Log-I/O im Hot Path.

Moegliche Messwerte:

- Calls
- gesamte und maximale Stopwatch-Ticks
- Verteilung ueber feste Buckets oder ein begrenztes Sample
- GC-Collection-Deltas und Heapgroesse pro Messfenster
- Queue-Laengen, Kandidatenzahlen oder Cache-Hit/Miss-Zaehler, falls die
  Bottleneck-Hypothese genau dies betrifft

Grenzen:

- jeder Patch veraendert den gemessenen Pfad
- sehr kleine Methoden koennen vom Timer dominiert werden
- Harmony-Inlining- und Patch-Reihenfolge muessen kontrolliert werden

Quelle: [Harmony-Dokumentation](https://harmony.pardeike.net/articles/intro.html).

### 4. WPR/ETW Sampling

Nutzen: systemweite, relativ wenig invasive CPU-Sicht auf Hauptthread, Unity Job
Worker, Scheduling, I/O und GC-nahe Pausen.

`wpr.exe` ist bereits in Windows vorhanden. Zum Auswerten der ETL-Datei fehlt
`wpa.exe`; es kommt mit dem Windows Performance Toolkit aus dem Windows ADK.

Staerken:

- Sampling statt Instrumentierung jeder Methode
- trennt Hauptthread und Worker-Auslastung
- hilfreich fuer Wartezeit in PathFinder-Jobs, CPU-Saettigung und fremde
  Hintergrundlast

Grenzen:

- Managed Mono-Symbolaufloesung kann unvollstaendig sein
- ohne WPA ist die lokale Aufnahme zwar moeglich, aber noch nicht sinnvoll
  analysierbar
- fuer RimWorld-Methoden weniger bequem als Dubs

Quelle: [Microsoft WPR/WPA](https://learn.microsoft.com/en-us/troubleshoot/windows-server/support-tools/support-tools-xperf-wpa-wpr).

### 5. Unity Profiler, experimentell

Unity unterstuetzt Player-Start mit `-profiler-enable` und Ausgabe in eine Raw-
Datei. Die regulaere Attach-Dokumentation setzt jedoch einen Development Build
voraus. Die installierte RimWorld-Version ist ein fertiger Release-Player, und
der passende Unity Editor/Standalone Profiler ist nicht installiert.

Wir installieren Unity deshalb nicht vorsorglich. Ein spaeterer kurzer Test mit
Profiler-CLI-Flags darf nur in den Workspace schreiben und muss zuerst zeigen,
ob dieser Release-Player verwertbare Daten liefert.

Quelle: [Unity Profiling Applications](https://docs.unity3d.com/2022.2/Documentation/Manual/profiler-profiling-applications.html).

## Nicht passende Werkzeuge

- `dotnet-trace` und `dotnet-counters` zielen auf CoreCLR/EventPipe. RimWorld
  laeuft hier unter Unity Mono und ist daher kein passendes Attach-Ziel.
- dnSpyEx ist als Debugger/Browser optional, aber fuer den reproduzierbaren
  Decompile- und Messpfad nicht erforderlich.
- Rider und Visual Studio sind fuer Navigation komfortabel, liefern allein aber
  noch keine bessere RimWorld-Tickmessung.
- Der Unity Editor ist fuer einen Harmony-Mod weder Build- noch Laufzeit-
  Voraussetzung.

## Empfohlener Messpfad

```text
Built-in Benchmark
  -> Dubs grobe Kategorie
    -> Dubs enger Methodenast
      -> gezielte eigene Zaehler/Timer, falls noetig
        -> uninstrumentierter Built-in A/B-Benchmark
```

WPR kommt parallel hinzu, wenn Hauptthread-Wartezeit, Job-Worker-Auslastung oder
systemfremde CPU-Last die Interpretation blockieren.
