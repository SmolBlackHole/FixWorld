# Roadmap: RimWorld Performance Research

Status: **freigegeben für Phase A1/A2**  
Stand: 2026-08-30  
Planrevision: 2  
Primärer Spielbuild: `1.6.4871 rev591`  
`Assembly-CSharp.dll`: `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`

Nach Freigabe ist diese Datei der verbindliche Rahmen für die weitere Arbeit.
Eine Phase beginnt erst, wenn ihre Voraussetzungen erfüllt sind. Neue
Erkenntnisse dürfen Implementierungsdetails ändern, aber keine Mess-Gates,
Verhaltensverträge oder Phasengrenzen stillschweigend umgehen.

## Ziel

Ein reproduzierbarer Forschungs- und Entwicklungsablauf soll für eine
synthetisch erzeugte, komplexe Kolonie des exakten Zielbuilds:

1. die tatsächliche TPS-/CPU-Begrenzung messen,
2. den dominanten Methodenpfad mit möglichst wenig Messverzerrung eingrenzen,
3. genau eine kleine, verhaltensneutrale Optimierung als externen Harmony-Patch
   prüfen,
4. den Effekt in getrennten, uninstrumentierten A/B-Läufen nachweisen oder den
   Ansatz verwerfen.

Ein Mod, eine Sammlung vermeintlich langsamer Methoden oder eine Veröffentlichung
sind noch kein Ziel. Der erste belastbare Engpass entscheidet über den ersten
Patch.

## Stand gegen den Originalauftrag

| Ursprüngliche Phase | Status | Nachweis |
| --- | --- | --- |
| 1. Installation untersuchen | abgeschlossen | `notes/installation.md` |
| 2. Werkzeuge bestimmen | abgeschlossen | Scoop-SDK und lokales ILSpy genügen; Visual Studio und Unity sind nicht erforderlich |
| 3. exakten Build dekompilieren | abgeschlossen | 9.218 Dateien, reproduzierbarer Hash-Guard und Provenienz |
| 4. Simulationsarchitektur verstehen | initial abgeschlossen | Tick-, Pawn-, Job-, Path-, Map- und World-Pfade sind kartiert; weitere Tiefenanalyse folgt nur für gemessene Kandidaten |
| 5. Profiling-Workflow | Entwurf abgeschlossen, Ausführung offen | Strategie und Benchmark-Protokoll existieren, aber noch keine Fixture-Messdaten |
| 6. Harmony-Eignung | abgeschlossen | `net472`/x64-PoC baut und läuft im isolierten Quicktest |
| 7. erste Optimierung | absichtlich nicht begonnen | Es gibt noch keinen gemessenen Hot Path |

Damit ist der vorbereitende Forschungsdurchlauf abgeschlossen. Der kritische
Pfad beginnt jetzt bei einer reproduzierbaren Fixture-Suite, nicht bei weiterer
breiter Quelltextsuche.

Ein fremder Late-Game-Spielstand ist keine Voraussetzung mehr. Wenn später ein
passender, sauber ladbarer Spielstand auftaucht, dient er als externe
Gegenprobe. Die Optimierungsentscheidung entsteht zuerst aus dem kontrollierten
synthetischen Fixture.

## Verbindliche Grenzen

- Die RimWorld-Binaries und Spieldaten unter der Steam-Installation bleiben
  read-only. Einzige bestehende Ausnahme ist der freigegebene Mod-Junction.
- `decompiled/Assembly-CSharp/` ist urheberrechtlich geschützte, lokale
  Referenz. Der Inhalt wird weder bearbeitet noch committed oder veröffentlicht.
- Eigener Code bleibt unter `mod/`; lokale Saves, Captures, Logs und Resultate
  bleiben in den bereits ignorierten Verzeichnissen.
- Baseline, Diagnose und A/B-Validierung sind getrennte Laufarten. DPA-Werte
  werden nicht als unverzerrte Gesamtperformance ausgegeben.
- Keine Optimierung ohne reproduzierbaren Engpass, konkrete Verhaltensinvariante
  und ausdrücklichen Optimierungsvorschlag.
- Kein breites Multithreading, keine Original-DLL-Änderung und kein
  architektonischer Umbau als erstes Experiment.
- Kein Commit, Release oder Upload weiterer Arbeit ohne gesonderten Auftrag.

## Stop- und Reset-Bedingungen

Die aktive Phase stoppt, wenn eine der folgenden Bedingungen eintritt:

- Der Hash von `Assembly-CSharp.dll` oder der Runtime-Build ändert sich. Dann
  müssen Inventur, Decompile-Provenienz, Patch-Kompatibilität und Baseline für
  den neuen Build zuerst aktualisiert werden.
- Ein eingefrorenes Fixture lädt nicht wiederholt aus derselben unveränderten
  Datei oder erzeugt bereits ohne unseren Patch relevante Exceptions.
- Zwei vermeintlich identische Baseline-Runs unterscheiden sich so stark, dass
  kein stabiles Messfenster definiert werden kann.
- Die Diagnoseinstrumentierung dominiert die gemessenen Kosten oder verändert
  das beobachtete Verhalten.
- Ein Patch verändert Spielverhalten, erzeugt Log-Spam, bricht Save/Load oder
  verbessert den uninstrumentierten Gesamtbenchmark nicht über die normale
  Streuung hinaus.

## Phase A: Aktive komplexe Kolonie erzeugen und einfrieren

Voraussetzung: Der exakte Zielbuild sowie Core, Royalty, Ideology, Biotech,
Anomaly und Odyssey sind verfügbar. Das ist auf der untersuchten Installation
erfüllt.

Eine hohe Pawnzahl allein gilt nicht als komplexe Kolonie. Das primäre Fixture
muss gekoppelte Spielsysteme tatsächlich beschäftigen. Ein Gebäude, Feld oder
Bill zählt nur dann zur Abdeckung, wenn daraus im Messfenster beobachtbare
Tick-, Job-, Such-, Pfad- oder Managerarbeit entsteht.

### A1: Aktivitätsvertrag festlegen

`complex-steady-v1` muss mindestens diese Lastdimensionen gleichzeitig abdecken:

| Dimension | Geplanter Zustand | Beobachtbarer Nachweis |
| --- | --- | --- |
| Zeitpläne und Needs | mehrere versetzte Arbeits-, Schlaf- und Freizeitgruppen | gleichzeitig arbeitende, essende, ruhende und sich erholende Pawns; dokumentierte Wechselzeiten |
| Jobs und WorkGiver | aktive Bills sowie Koch-, Herstell-, Reinigungs-, Reparatur-, Bau- und Versorgungsarbeit | laufende und neu vergebene Jobs aus mehreren WorkGiver-Klassen |
| Lagerung und Hauling | lokale Eingangs-/Ausgangslager plus gemeinsames Zentrallager mit unterschiedlichen Prioritäten | Haul-Jobs, Reservations und Materialbewegung über kurze und lange Routen |
| Pfade und Reachability | räumlich getrennte Wohn-, Arbeits-, Lager-, Freizeit- und Feldbereiche | fortlaufende PathRequests und Reachability-Prüfungen mit verschiedenen Distanzen |
| Türen, Räume und Temperatur | Korridore, alternative Routen, manuelle und angetriebene Türen, beheizte und gekühlte Räume | Türbewegungen, Raum-/Temperaturarbeit und tatsächlicher Pawn-Verkehr durch die Topologie |
| Elektrizität | mehrere getrennte Netze mit Erzeugern, Batterien und wechselnden Verbrauchern | aktive Netze mit nichttrivialer Erzeugung, Speicherung und Last |
| Felder und Pflanzen | mehrere Kulturen in verschiedenen Wachstumsstufen | gleichzeitig offene Sä-, Ernte- und Transportarbeit |
| Tiere und DLC-Systeme | Tiere, Mechs und ausgewählte aktive Royalty-, Ideology-, Biotech-, Anomaly- und Odyssey-Komponenten | laufende Needs, Jobs, Comps oder Manager der jeweils aufgenommenen Systeme |

Die spätere Inventur erfasst deshalb nicht nur Objektzahlen, sondern auch
aktuelle Jobverteilung, offene Bills, Reservationszahl, aktive PowerNets,
Türnutzung, Feldzustände sowie Path-/Reachability-Aktivität. Details, die sich
ohne Instrumentierung nicht belastbar zählen lassen, werden in Phase D mit DPA
oder kleinen hypothesenspezifischen Zählern ergänzt.

### A2: Ein aktives Koloniemodul bauen

Der bestehende Forschungsmod bekommt eine einmalige Debug-Aktion, die auf einer
frischen 250x250-Map ein reproduzierbares Koloniemodul erzeugt. Sie ist nach der
Erzeugung vollständig inaktiv und bleibt in Baseline und A/B identisch geladen.

Ein Modul verbindet:

- Bewohner mit versetzten Zeitplänen und sinnvoll gesetzten
  Arbeitsprioritäten,
- Schlaf-, Ess-, Freizeit- und Arbeitsräume hinter mehreren Türstrecken,
- Werkbänke mit ausführbaren Bills, vorhandenen Zutaten und erreichbaren
  Ausgabelagern,
- lokale Lager mit einem weiter entfernten Zentrallager, damit Hauling und
  Materialsuche nicht trivial werden,
- bepflanzte, erntereife und neu zu säende Felder,
- mindestens ein eigenes belastetes Stromnetz mit Erzeugung, Batterie,
  Verbrauchern und angetriebenen Türen,
- Tiere, Mechs und nur solche DLC-Komponenten, die nachweislich aktiv ticken oder
  Arbeit erzeugen.

RimWorlds `Make colony (full)` wird einmal separat als `catalog-control-v1`
gesichert. Es prüft die Breite vorhandener Defs und Generatorfehler, ist aber
nicht das Hauptfixture: Der Vanilla-Generator stellt viele Dinge in ein fixes
100x100-Rechteck, ohne eine realistische Liefer-, Wege- und Betriebsstruktur zu
garantieren.

### A3: Systemlast nur bei Bedarf gemeinsam skalieren

S1 ist der erste Kandidat für das Hauptfixture. Erfüllt dieses einzelne aktive
Modul den Aktivitätsvertrag, bleibt stabil und erreicht auf 3x nicht dauerhaft
das 360-TPS-Cap, wird es zunächst direkt als `complex-steady-v1` verwendet.
Größere Tiers werden nicht vorsorglich erzeugt.

Nur wenn S1 zu leicht ist oder eine Lastdimension nicht ausreichend beschäftigt,
vervielfacht die Lastleiter das ganze aktive Modul, nicht nur Pawns oder Tiere:

| Tier | Aktive Module | Ungefähre Pawn-Skalierung | Zweck |
| --- | ---: | ---: | --- |
| S1 | 1 | 24 Kolonisten plus Tiere und Mechs | Funktions- und Aktivitätsnachweis |
| S2 | 2 | 48 Kolonisten plus proportional mehr Arbeit und Infrastruktur | mittlere Systemlast |
| S3 | 4 | 96 Kolonisten plus proportional mehr Arbeit und Infrastruktur | schwere Dauerlast |
| S4 | 8 | 192 Kolonisten plus proportional mehr Arbeit und Infrastruktur | oberer Stresstest, nur wenn S3 zu leicht ist |

Zusatzmodule erhalten eigene Wohn-, Produktions-, Feld-, Tür- und Stromanteile,
teilen aber zentrale Lager und einzelne Dienste. Dadurch steigen WorkGiver-
Kandidaten, Reservationskonflikte, Pfaddistanzen und Querbewegungen mit der
Pawnzahl. Ein Tier darf nicht durch wartende oder untätige Pawns künstlich
schwer werden.

Falls bereits S1 unter die sinnvoll messbare TPS-Untergrenze fällt, entsteht
zusätzlich S0 mit denselben aktiven Systemklassen, aber halbierter Zahl
funktionaler Arbeits-, Lager-, Feld- und Bewohnereinheiten. Damit bleibt auch
dann eine strukturelle Skalierungsprobe erhalten.

Der erste ausreichende Tier wird `complex-steady-v1`. Ein größerer Tier entsteht
nur, wenn der vorherige die Auswahlkriterien verfehlt:

- drei Neustart-Runs laden, wärmen auf und speichern wieder ohne neue Fehler,
- die geforderten Systemklassen erzeugen im Messfenster nachweislich Arbeit,
- der eingebaute 30-Sekunden-Benchmark bleibt bedienbar,
- 3x erreicht nicht dauerhaft das 360-TPS-Cap,
- der Median liegt vorzugsweise zwischen 60 und 300 TPS,
- die Median Absolute Deviation liegt höchstens bei 10 Prozent des Medians.

### A4: Dauerlast und Zustandswechsel trennen

Aus demselben ausgewählten Tier entstehen zwei primäre Saves:

- `complex-steady-v1`: versetzte Zeitpläne und ein stabiler, dauerhaft
  beschäftigter Koloniebetrieb für die Gesamtbaseline.
- `complex-shift-v1`: direkt vor einem reproduzierbaren größeren
  Zeitplanwechsel gespeichert. Dieser Lauf misst Jobabbrüche, neue
  WorkGiver-Suchen, Türverkehr und Pfadanfragen als realen Lastburst.

Feuer, Großraid, Bau-/Abrisswellen und Änderungen an Strom- oder
Raumtopologien werden nicht in diese beiden Baselines gemischt. Sie entstehen
später nur dann als eigene eingefrorene Ereignis-Fixtures, wenn der gemessene
Hot Path davon abhängt.

### A5: Fixtures einfrieren

1. Jeden ausgewählten Save unter `benchmarks/saves/` ablegen, ohne ihn in
   späteren Runs zu überschreiben.
2. SHA-256, Save-Tick, RimWorld-Build, DLCs, Modliste, Modreihenfolge,
   Generatorparameter, Modulzahl und relevante Mod-DLL-Hashes erfassen.
3. Eine feste Map, Kameraposition, Zoomstufe und einen reproduzierbaren
   Aktivitätszustand für 1x/2x/3x bestimmen.
4. `Player.log` und den sichtbaren Zustand nach Laden, Warm-up und erneutem
   Laden prüfen.

Wahrscheinliche Artefakte:

- eigener, nach Erzeugung inaktiver Fixture-Generator unter `mod/`
- ignoriert: `benchmarks/saves/`, `benchmarks/results/<fixture-id>/`
- versionierbar: Fixture-Rezept und Aktivitätsinventur in
  `notes/research-log.md`

Akzeptanz:

- `complex-steady-v1`, `complex-shift-v1` und `catalog-control-v1` besitzen
  eindeutige Hashes und Erzeugungsparameter. Falls skaliert wurde, gilt das auch
  für den direkt kleineren Systemtier.
- Die Systemabdeckung ist durch Aktivität statt nur durch vorhandene Objekte
  belegt.
- Die Ausgangszustände laden wiederholt ohne fehlende Abhängigkeiten oder neue
  Exceptions.
- Warm-up-Start, Messkarte und Aktivitätszustand sind eindeutig beschrieben.
- Kein Ausgangssave wird bei einem Run überschrieben.

Ein gefundener fremder Late-Game-Save darf später als sekundäres Fixture
aufgenommen werden, wenn Build, DLCs, Mods und Logzustand verifizierbar sind. Er
ersetzt die synthetischen Fixtures nicht.

Recherchebefund: Der auffindbare öffentliche 10.000-Kolonisten-Benchmark wurde
für RimWorld 1.4 gebaut und friert laut Autor bereits nach wenigen hundert
Ticks ein. Aktuelle Creator-Archive bieten zwar Spielstände an, aber ohne einen
vorab verifizierbaren Match für unseren exakten Build und DLC-Zustand. Diese
Quellen bleiben deshalb Kandidaten für externe Gegenproben, nicht für die
primäre Baseline:

- [RimWorld Benchmark 1.4 mit Download](https://www.reddit.com/r/RimWorld/comments/xwn17b/rimworld_benchmark_14_unstable/)
- [Adam Vs Everything: RimWorld-Downloads](https://adamvseverything.com/en-usd/pages/downloads)

## Phase B: Diagnoseumgebung herstellen

Aktueller Zustand: Harmony ist vorhanden; Dubs Performance Analyzer ist lokal
noch nicht installiert. Das offizielle Workshop-Item `2038874626` und das
Upstream-Repository enthalten weiterhin einen 1.6-Build.

Arbeit:

1. Dubs Performance Analyzer über das offizielle Workshop-Item installieren.
2. Package-ID, 1.6-DLL, Version und SHA-256 dokumentieren.
3. Zwei explizite Modkonfigurationen vorbereiten:
   - Baseline: Core, DLCs und gegebenenfalls der nach Erzeugung inaktive
     Fixture-Generator; kein DPA-Profiling, kein Performance-Patch
   - Diagnose: gleiche Basis plus Harmony und DPA
4. Save in beiden Konfigurationen laden und Logs vergleichen.
5. DPA zunächst ohne Stacktraces und ohne breites Internal Profiling prüfen.

Quellen:

- [Steam Workshop: Dubs Performance Analyzer](https://steamcommunity.com/sharedfiles/filedetails/?id=2038874626)
- [Upstream-Repository](https://github.com/Dubwise56/Dubs-Performance-Analyzer)

Akzeptanz:

- Beide Modkonfigurationen sind reproduzierbar und eindeutig benannt.
- DPA zeigt Messwerte für das Fixture, ohne neue wiederholte Exceptions.
- Der Baseline-Pfad bleibt ohne DPA-Instrumentierung startbar.

## Phase C: Uninstrumentierte Baseline aufnehmen

Arbeit:

1. Das bestehende Protokoll aus `benchmarks/protocol.md` auf
   `complex-steady-v1` ausführen.
2. Pro Geschwindigkeitsstufe mindestens drei vollständige Neustart-Runs:
   1x/60 TPS, 2x/180 TPS und 3x/360 TPS.
3. Je Run 3.600 Ticks Warm-up und danach den eingebauten 30-Sekunden-Benchmark.
4. Rohwerte, Start-/Endtick, FPS, TPS, Ticks/Frame, Screenshot und Log sichern.
5. Median, Minimum/Maximum und Median Absolute Deviation je Stufe berechnen.
6. `complex-shift-v1` mindestens dreimal aus demselben Starttick auf 3x messen.
7. Falls skaliert wurde, den direkt kleineren Systemtier mindestens dreimal auf
   3x messen.

Akzeptanz:

- Mindestens neun gültige Dauerlast-Runs und drei Shift-Runs liegen vor. Falls
  skaliert wurde, kommen drei Runs des direkt kleineren Tiers hinzu.
- Der 3x-Inaktivitätsboost ist verhindert oder als eigener Zustand getrennt.
- Streuung und eventuelle TPS-Deckelung sind sichtbar, nicht wegaggregiert.
- Es existiert ein belastbarer Referenzwert, gegen den später A/B getestet wird.
- Falls mehrere Tiers nötig waren, zeigt der direkt kleinere Systemtier, wie der
  Hot Path mit der gesamten Koloniestruktur skaliert. Pawnzahl wird nicht
  isoliert als Ursache behandelt.

## Phase D: Grobes DPA-Profil erstellen

Arbeit:

1. Den CPU-gesättigten, aber stabilsten Baseline-Zustand als primäres
   Diagnosefenster verwenden, voraussichtlich 3x.
2. Zuerst grobe Tick-/Update-Kategorien messen.
3. Zeit, Anteil, Maximum und Call Count gemeinsam betrachten.
4. Tick-Arbeit von pro Frame laufenden `MapUpdate`-/Rendering-Kosten trennen.
5. Dauerlast und Shift-Burst getrennt profilieren und nicht zu einem Mittelwert
   vermischen.
6. Die drei größten stabilen Beiträge dokumentieren und Vanilla-, DLC- und
   Fremdmod-Code unterscheiden.

Akzeptanz:

- Mindestens ein dominanter Ast ist in wiederholten Fenstern sichtbar.
- Seine absolute Zeit und Call Counts erklären einen relevanten Anteil des
  beobachteten TPS-Problems.
- Instrumentierungs-Overhead und Messfenster sind dokumentiert.
- Es wird noch kein Patch geschrieben.

## Phase E: Engpass und Hypothese eingrenzen

Arbeit:

1. Nur den dominanten Ast mit DPA weiter aufteilen.
2. Den gemessenen Methodenpfad im exakten dekompilierten Build nachvollziehen.
3. Skalierungsfaktor erfassen, zum Beispiel Pawnzahl, Kandidatenzahl,
   PathRequests, Cache-Hits/Misses oder Collection-Größe.
4. Nur wenn DPA die Frage nicht beantworten kann, kleine eigene
   Harmony-Zähler/Timer ohne Hot-Path-Logging planen und separat messen.
5. Eine falsifizierbare Hypothese formulieren: welche wiederholte Arbeit ist
   redundant, welche Invariante erlaubt ihre Vermeidung und wodurch wird sie
   ungültig?

Akzeptanz:

- Hot Path, Aufrufer, Call Count und kostentreibende Eingabegröße sind belegt.
- Die Hypothese benennt korrekte Invalidierungsereignisse oder erklärt, warum
  kein Cache erforderlich ist.
- Der erwartete Nutzen ist gross genug, um einen externen Patch zu rechtfertigen.
- Falls keine belastbare Hypothese entsteht, geht es zurück zu Phase D.

## Gate: Optimierungsvorschlag

Vor jeder Verhaltensänderung entsteht ein kurzer Vorschlag unter
`notes/optimizations/<slug>.md` mit:

- Messdaten und reproduzierbarem Profil
- betroffenem RimWorld-Pfad und Patchpunkt
- zu erhaltendem Verhalten
- kleinstem vorgesehenen Eingriff
- Invalidierung/Lebensdauer eigener Daten
- Harmony-Kompatibilität und Patchreihenfolge
- gezielten Korrektheitsprüfungen
- Rückbaukriterium

Dieser Vorschlag braucht eine neue Freigabe. Die Roadmap allein autorisiert
keinen heute noch unbekannten Optimierungspatch.

## Phase F: Genau eine Optimierung implementieren

Arbeit nach Freigabe des Vorschlags:

1. Nur den genehmigten Hot Path im externen Mod ändern.
2. Originalassemblies unangetastet lassen.
3. Diagnosecode standardmäßig deaktivieren oder klar vom Patch trennen.
4. Build, Assembly-Referenzen, Harmony-Patchziel und isolierten Ladetest prüfen.
5. Keine benachbarten Refactorings oder zweiten Optimierungen aufnehmen.

Akzeptanz:

- Release-Build: 0 Fehler und keine neuen Warnungen.
- Harmony patcht exakt den vorgesehenen Methoden-Build.
- Mod lädt ohne Patch-/Assemblyfehler.
- Der definierte Verhaltensvertrag ist in fokussierten Tests erhalten.

## Phase G: A/B und Korrektheit validieren

Arbeit:

1. DPA für die Gesamtmessung deaktivieren.
2. A: Performance-Patch aus, B: genau dieser Patch an.
3. Runs gepaart oder in wechselnder Reihenfolge ausführen, ansonsten identische
   Save-, Mod-, Kamera-, Warm-up- und Geschwindigkeitsbedingungen.
4. Bei kleinen Effekten mindestens fünf gültige Runs je Variante.
5. Neben `complex-steady-v1` auch `complex-shift-v1` oder das später
   hot-path-spezifisch erzeugte Ereignis-Fixture vergleichen.
6. Save/Load, Jobs, Reservations, Pathfinding/Reachability, Kampf,
   Spawn/Tod, relevante DLC-Mechaniken und mehrere Ingame-Tage prüfen.

Akzeptanz:

- Der Median verbessert sich stärker als die normale Baseline-Streuung.
- Die Verbesserung ist im uninstrumentierten eingebauten Benchmark sichtbar.
- Keine neue Exception, kein Log-Spam und keine relevante
  Verhaltensabweichung tritt auf.
- Andernfalls wird der Patch verworfen oder als nicht bewiesen markiert.

## Phase H: Entscheidung und nächste Iteration

Mögliche Ergebnisse:

- **Behalten:** Messung und Korrektheit sind belastbar; Dokumentation und
  Forschungslog werden aktualisiert.
- **Überarbeiten:** Die Hypothese stimmt, aber Implementierung oder
  Invalidierung ist fehlerhaft; zurück zu Phase F mit demselben Scope.
- **Verwerfen:** Effekt liegt in der Streuung, Verhalten weicht ab oder der
  Hot Path war falsch attribuiert; Patch entfernen und zu Phase D/E zurück.

Erst nach einem akzeptierten ersten Patch wird entschieden, ob der PoC einen
dauerhaften Modnamen, Einstellungen, Packaging oder weitere Optimierungen
braucht. WPR/WPA, Unity Profiler, Veröffentlichung und breite
Kompatibilitätsarbeit bleiben bis zu einem konkreten Bedarf aufgeschoben.

## Nächster konkreter Schritt nach Freigabe

Phase A1/A2 starten: den Aktivitätsvertrag in ein konkretes Koloniemodul
übersetzen und im bestehenden Forschungsmod eine einmalige Erzeugungsaktion
bauen. Parallel wird RimWorlds eingebaute Vollkolonie nur als
`catalog-control-v1` gesichert. Liefert S1 bereits aktive und ausreichend hohe
Systemlast, wird es direkt genutzt. Nur andernfalls wird zu S2 bis maximal S4
skaliert. Es wird weiterhin keine Optimierung ausgewählt.
