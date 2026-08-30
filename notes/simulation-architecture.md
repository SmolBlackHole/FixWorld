# Tick- und Simulationsarchitektur

Grundlage: lokal dekompilierte `Assembly-CSharp.dll`, SHA-256
`5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`.

Diese Notiz beschreibt beobachtete Aufrufpfade. Sie bewertet noch keinen Pfad
als Engpass.

## Einstieg pro Frame

Der Unity-Frame beginnt fuer das Spiel in `Verse.Root_Play.Update()`:

```text
Unity Update
  Root_Play.Update
    Game.UpdatePlay
      TickManager.TickManagerUpdate
        0..n x DoSingleTick
      LetterStackUpdate
      WorldUpdate
      MapUpdate je Map
      GameComponentUpdate
```

Quellen: `Verse/Root_Play.cs`, `Verse/Game.cs`, `Verse/TickManager.cs`.

Simulation und Darstellung sind damit gekoppelt, aber nicht identisch. Ein
Frame kann mehrere Simulationsticks ausfuehren. `MapUpdate()` laeuft pro Frame
und enthaelt unter anderem Power-Verbindungsupdates, Region-/Raum-Rebuilds,
Glow-Updates und visuelle Nacharbeit. Solche Kosten koennen FPS beeinflussen,
ohne direkt in einem Thing-Tick zu liegen.

## Tick-Pacing und Ziel-TPS

`TickManager` verwendet eine Basis von 60 Ticks pro Sekunde und folgende
Multiplikatoren:

| Spielgeschwindigkeit | Multiplikator | regulaeres Ziel |
|---|---:|---:|
| pausiert | 0 | 0 TPS |
| 1x | 1 | 60 TPS |
| 2x | 3 | 180 TPS |
| 3x | 6 | 360 TPS |
| 3x ohne relevante Aktivitaet | 12 | 720 TPS |
| Dev-Ultrafast mit Map | 15 | 900 TPS |

Ohne geladene Map gelten noch hoehere Sonderwerte. Pro Frame werden hoechstens
`2 * Multiplikator` Ticks versucht. Nach etwa 45,45 ms Tick-Arbeit bricht die
Schleife ab und verwirft aufgelaufene Restzeit. Deshalb kann das Spiel TPS
drosseln, um die Darstellung nicht unbegrenzt weiter zu blockieren.

Fuer Benchmarks muss der Schlaf-/Inaktivitaets-Boost kontrolliert werden, sonst
sind zwei vermeintliche 3x-Laeufe nicht vergleichbar.

## Reihenfolge eines Simulationsticks

`TickManager.DoSingleTick()` arbeitet im Wesentlichen in dieser Reihenfolge:

1. `MapPreTick()` fuer jede Map
2. Spieltick erhoehen
3. normale, seltene und lange `TickList`
4. Datum, Szenario und `WorldTick()`
5. StoryWatcher, GameEnder, Storyteller, Tales und Quests
6. `WorldPostTick()`
7. `MapPostTick()` fuer jede Map
8. History-, GameComponent-, Letter-, Autosave-, Filth- und Transport-Ticks

Der Storyteller wird zwar pro Tick aufgerufen, erzeugt seine regulaere
Incident-Pruefung aber nur alle 1.000 Ticks. Viele weitere Manager sind aehnlich
intern intervallgesteuert. Ein Aufruf pro Tick beweist daher noch keine hohe
Arbeit pro Tick.

## Thing-Ticks und Frequenzen

`TickList` haelt getrennte, gehashte Buckets:

| `TickerType` | Intervall | Verhalten |
|---|---:|---|
| `Normal` | 1 | Bucket und `Thing.DoTick()` in jedem Spieltick |
| `Rare` | 250 | pro Tick ein anderer Bucket, jedes Thing etwa alle 250 Ticks |
| `Long` | 2.000 | pro Tick ein anderer Bucket, jedes Thing etwa alle 2.000 Ticks |
| `Never` | - | nicht registriert |

Registrierungen und Abmeldungen werden am Beginn des naechsten `TickList.Tick()`
eingepflegt. `IThingHolder` wird unabhaengig vom Def-Ticker in der normalen Liste
gefuehrt, damit enthaltene Dinge korrekt getickt werden koennen.

Bei normalen Things trennt `Thing.DoTick()` zwei Ebenen:

- `Tick()` laeuft weiterhin jeden Tick.
- `TickInterval(delta)` fasst weniger zeitkritische Arbeit in Abstaenden von 1
  bis 15 Ticks zusammen. Der Abstand ist kamerabasiert; Tiere erzwingen 15.

`ThingWithComps` verteilt diese Aufrufe an `CompTick`, `CompTickInterval`,
`CompTickRare` und `CompTickLong`.

## Pawn, Jobs, Needs und Gesundheit

Ein nicht suspendierter Pawn verarbeitet in `Pawn.Tick()` unter anderem:

- Pfadbewegung, Verbs und Stances fuer gespawnte Pawns
- aktuellen Job-Driver
- Health/Hediffs
- Equipment, Abilities, Inventory und DLC-Tracker
- einige visuelle und akustische Wartungen

Im gebuendelten `Pawn.TickInterval(delta)` liegen unter anderem Job-, Health- und
MindState-Intervalle, Needs, Interaktionen, Skills, Beziehungen, Alter und
Records. Needs rufen ihre `NeedInterval()` gehashte alle 150 Ticks auf.
`Pawn.TickRare()` laeuft etwa alle 250 Ticks und behandelt beispielsweise
Apparel, Training und einen Teil des Waermeeintrags.

`Pawn_JobTracker` sucht nicht in jedem Tick pauschal einen neuen Job. Die
ThinkTree-Auswertung startet vor allem, wenn kein aktueller Job existiert oder
ein Override geprueft wird. `JobGiver_Work` geht dann priorisierte WorkGiver
durch. Scanner verwenden entweder eigene Kandidatenmengen oder die bereits nach
Def/Gruppe indizierten `ListerThings`-Listen und kombinieren dies mit
`GenClosest`, Reachability und WorkGiver-spezifischen Pruefungen.

Die moegliche Skalierung ist damit kontextabhaengig: Anzahl gleichzeitig
jobsuchender Pawns, Zahl der aktiven WorkGiver, Kandidatenmenge und Zahl der
Reachability-Pruefungen. Erst Call Counts und Zeitmessung koennen zeigen, welcher
Faktor im Testsave wirklich dominiert.

## Pfadsuche und Reachability

RimWorld 1.6 hat hier echte Parallelisierung:

- `PathFinder.PushRequest()` stellt Anfragen in eine Queue.
- `MapPreTick()` ruft `PathFinderTick()` auf.
- Zu Beginn werden Jobs des vorherigen Durchlaufs abgeschlossen und Ergebnisse
  an die Anfragen zurueckgegeben.
- Danach werden Map-Grid-Daten gesammelt, Grid-Jobs und gebuendelte Path-Jobs
  ueber Unity Jobs geplant und mit Burst-faehigen Jobtypen ausgefuehrt.
- Am naechsten Synchronisationspunkt wartet der Hauptthread gegebenenfalls auf
  noch laufende Jobs.
- `FindPathNow()` bleibt ein synchroner Sonderpfad: benoetigte Jobs werden sofort
  abgeschlossen beziehungsweise direkt ausgefuehrt.

Die Pfadsuche ist also parallelisiert, aber nicht kostenlos oder voellig vom
Hauptthread entkoppelt. Messpunkte muessen Queue-Arbeit, Datenaufbereitung,
Worker-Zeit und Wartezeit am `CompleteAll` unterscheiden.

`Reachability.CanReach()` ist ein separater synchroner Region-/District-Graph-
Pfad. Er besitzt Schnellpfade und einen District-basierten Cache. Die Existenz
eines Reachability-Aufrufs bedeutet daher weder automatisch Flood-Fill noch
automatisch Cache-Hit. Cache-Invalidierung bei Topologieaenderungen gehoert zur
Messung.

## Map-Systeme

`MapPreTick()` umfasst unter anderem Item-Verfuegbarkeit, Haulables, Roof-
Nacharbeit, Wind, Temperatur und Pathfinder-Scheduling.

`MapPostTick()` ruft unter anderem Tier-/Pflanzen-Spawn, PowerNets,
Umwelteffekte, temporaeres Terrain, Gas, Pollution, Lords, Conditions, Wetter,
Ressourcen, Feuer, Flecks und MapComponents auf.

Wichtige interne Unterschiede:

- `MapTemperatureTick()` gleicht Raumtemperatur nur alle 120 Ticks ab und hat
  weitere 60-Tick-Arbeit.
- `PowerNetsTick()` iteriert pro Tick ueber alle PowerNets; die konkrete Arbeit
  liegt in `PowerNetTick()`.
- Region- und Raumdaten werden in `MapUpdate()` nur verarbeitet, wenn Zellen als
  dirty markiert sind. Rebuild-Spitzen sind daher ereignisgetrieben und eher in
  Frame- als reinen Tick-Profilen sichtbar.
- `ListerThings` pflegt Listen nach Def und RequestGroup samt State-Hashes. Viele
  globale Anfragen scannen daher eine Teilmenge, nicht blind alle Map-Things.
- `ReservationManager` verwaltet eine Reservationsliste; Kosten haengen von
  Reservationszahl und Aufrufhaeufigkeit ab.

## Welt-Simulation

`World.WorldTick()` tickt lebende World Pawns, Factions, WorldObjects, den
World-PathGrid, WorldComponents und Ideologien. Lebende World Pawns werden in
jedem World-Tick ueber `Pawn.DoTick()` verarbeitet. Mothball-Verarbeitung laeuft
alle 15.000 Ticks. WorldObjects werden ebenfalls aus einer wiederverwendeten
Temporaerliste getickt.

Das macht World Pawns zu einem sinnvollen Profiling-Kandidaten in alten Saves,
aber nicht zu einem bereits bewiesenen Engpass.

## Synchronitaet und vorhandene Parallelisierung

Der Ablauf in `TickManager.DoSingleTick()`, die meisten Manager, Pawn-Ticks,
ThinkTrees und Reachability laufen sequenziell auf dem Hauptthread. Beobachtete
Ausnahmen sind:

- Unity Jobs/Burst fuer die neue gebuendelte Pfadsuche und Grid-Aufbereitung
- Unity Jobs fuer Teile des Dynamic Drawings und Glow-Berechnungen
- ThreadPool-Arbeit beim Fleck-Zeichnen
- Worker-Threads fuer Lade-, Def- und Generierungsphasen

Rendering- und Ladeparallelisierung darf nicht als Beleg fuer parallel laufende
Pawn-AI oder allgemeine Multithread-Simulation interpretiert werden.

## Kandidaten fuer die erste Profiling-Runde

Diese Reihenfolge ist eine Messstrategie, keine Optimierungsrangliste:

1. Gesamte `DoSingleTick()`-Zeit und TPS bei 1x, 2x und 3x
2. normale `TickList`, aufgeteilt nach Pawn/Thing-Typ und Call Count
3. Pawn JobTracker, ThinkTree und einzelne WorkGiver
4. Health/Hediffs und Needs, jeweils Zeit plus Aufrufzahl
5. `PathFinderTick()`, Worker-Auslastung, Hauptthread-Wartezeit und
   `FindPathNow()`
6. Reachability und `GenClosest`, inklusive Cache-Hit-nahem Vergleich
7. `MapPreTick()`/`MapPostTick()`-Manager, besonders Power, Gas, Temperatur und
   MapComponents
8. WorldPawns und WorldObjects
9. pro Frame laufendes `MapUpdate()`, um FPS- von TPS-Problemen zu trennen
10. Allokationsrate und GC-Pausen waehrend derselben Messfenster

Erst nach dieser Aufteilung lohnt sich die Suche nach konkreten LINQ-Aufrufen,
Collection-Scans, Sortierungen, Cache-Invalidierung oder Dictionary/List-Churn.
