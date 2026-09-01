# FixWorld Runtime

Status: **freigegeben, Revision 1, Phase 1 abgeschlossen, Phase 2 nicht begonnen**

Baseline: `94fe7cf fix: harden early loader handoff`

## Ziel

FixWorld wird beim Prozessstart über Doorstop aktiv und stellt seine Infrastruktur
bereit, bevor RimWorld Mods lädt. Der frühe Loader bleibt klein. Eine eigene
Runtime besitzt anschließend Mod-Boot, Stages, Scheduling, Events, Telemetrie und
Caching. Die reguläre FixWorld-Mod wird auf Einstellungen, UI und die Verbindung
zum `ModContentPack` reduziert.

Der Umbau ist zunächst verhaltensgleich. Erst nachdem die neue Assembly-Grenze mit
der vollständigen Modliste stabil ist, werden weitere RimWorld-Stages ersetzt oder
parallelisiert.

## Zielarchitektur

```text
RimWorld.exe
└─ Doorstop
   └─ FixWorld.Preloader.dll
      └─ FixWorld.Loader.dll
         ├─ RimWorld-Vertrag prüfen
         ├─ früh geladene Harmony validieren
         ├─ FixWorld.Runtime.dll laden
         └─ FixWorldRuntime.StartEarly()

FixWorld.Runtime.dll
├─ Lifecycle und EventBus
├─ Scheduler, Worker und Main-Thread-Dispatcher
├─ Stage-Pipeline und Mod-Boot
├─ Diagnose und Telemetrie
├─ Cache-Infrastruktur
└─ RimWorld- und Harmony-Integration

Während CreateModClasses():
└─ FixWorld.Mod.dll
   ├─ FixWorldMod : Mod
   ├─ Einstellungen und UI
   ├─ ModContentPack-Anbindung
   └─ FixWorldRuntime.AttachMod(...)
```

## Ownership

### `FixWorld.Preloader`

- wird ausschließlich durch Doorstop geladen
- erkennt den RimWorld-Prozess und wartet auf `Assembly-CSharp`
- findet und lädt die installierte Harmony-Mod
- startet `FixWorld.Loader`
- enthält keine Mod-Boot-, Scheduler-, Cache- oder UI-Logik

### `FixWorld.Loader`

- validiert RimWorld-Version, Assembly-MVID und benötigte Signaturen
- lädt genau eine `FixWorld.Runtime.dll`
- ruft genau einen frühen Runtime-Einstieg auf
- fällt vor der ersten FixWorld-Mutation sicher auf Vanilla zurück
- enthält nach dem Cutover keinen eigenen Mod-Boot-Coordinator

### `FixWorld.Runtime`

- ist der einzige Besitzer der FixWorld-Infrastruktur
- startet früh EventBus, Scheduler, Dispatcher und Telemetrie
- besitzt Stage-Reihenfolge, Barrieren, Worker-Jobs und Main-Thread-Commits
- besitzt den Mod-Boot-Coordinator
- kapselt alle Aufrufe an RimWorld und Harmony hinter expliziten Adaptern
- wird pro Prozess genau einmal gestartet und geordnet beendet

### `FixWorld.Mod`

- bleibt als normaler Eintrag in der RimWorld-Modliste
- besitzt `About.xml`, Abhängigkeiten, Einstellungen und Mod-UI
- übergibt `ModContentPack` und einen unveränderlichen Settings-Snapshot an die
  bereits laufende Runtime
- startet keine zweite Infrastruktur und installiert keine alternativen Hooks

## Verbindliche Entscheidungen

- FixWorld liefert keine eigene `0Harmony.dll` aus. Die installierte Harmony-Mod
  bleibt die einzige Harmony-Engine im Prozess.
- Harmony wird zunächst instrumentiert, nicht ersetzt oder geforkt.
- Harmony-Aufrufe bleiben synchron. FixWorld darf sie nicht pauschal verzögern,
  weil Mods unmittelbar nach `Patch()` oder `PatchAll()` installierte Patches
  erwarten können.
- Der Loader wird kein zweites Runtime-Monolith. Infrastruktur gehört in
  `FixWorld.Runtime`.
- Der heutige späte Bootstrap wird direkt ersetzt. Es entsteht kein dauerhafter
  paralleler `early`-/`late`-Kompatibilitätspfad.
- Die Mod-Reihenfolge und das von Mods beobachtbare synchrone Verhalten bleiben
  erhalten, bis eine spätere Phase eine Änderung ausdrücklich freigibt.
- Keine RimWorld-DLL wird auf Disk verändert. FixWorld lädt eigene Assemblies und
  übernimmt Methoden nur im Prozess.
- Eine öffentliche Plugin-API wird noch nicht versprochen. Die Runtime verwendet
  ihre typisierten Stages und Events zunächst intern. Ein separates öffentliches
  Contracts-Paket entsteht erst mit einem echten externen Nutzer.

## Runtime-Lebenszyklus

```text
NotStarted
→ Starting
→ EarlyReady
→ ModAttached
→ Running
→ Stopping
→ Stopped

Starting / EarlyReady / ModAttached
→ Failed
```

Die Zustände bedeuten:

- `EarlyReady`: EventBus, Scheduler, Dispatcher, frühe Telemetrie und der
  Mod-Boot-Hook sind bereit.
- `ModAttached`: `ModContentPack`, Settings-Snapshot, DDS-Konfiguration und
  UI-Brücke sind verbunden.
- `Running`: Play-Data-Loading ist abgeschlossen; Deferred- und Background-Jobs
  dürfen anhand ihrer Budgets laufen.
- `Failed`: früher Start oder Attach ist fehlgeschlagen. Der Zustand ist für den
  Prozess terminal.
- `Stopped`: Shutdown ist abgeschlossen. Die Runtime darf nicht neu entstehen.

`StartEarly()` ist idempotent. Wiederholte Aufrufe erzeugen weder Infrastruktur
noch Hooks erneut. `AttachMod()` mit derselben Modinstanz ist ebenfalls
idempotent; eine andere Instanz im selben Prozess ist ein Vertragsfehler. Nach
`Failed`, `Stopping` oder `Stopped` sind Start, Attach und neue Jobs unzulässig.
Alle Zustandswechsel werden an einer Runtime-eigenen Synchronisationsgrenze
atomar ausgeführt und als unveränderlicher Snapshot lesbar gemacht.

`StartEarly()` darf keine noch nicht initialisierten Unity- oder veränderlichen
Verse-Zustände anfassen. Reine Datei-, Index-, Diagnose- und Scheduler-Arbeit ist
zulässig. Unity-Objekte und modabhängige Arbeit beginnen erst an einer dafür
deklarierten Stage.

RimWorld-Hot-Reload erzeugt keine zweite Runtime. Scheduler, Events, Telemetrie
und Caches bleiben im selben AppDomain bestehen; modabhängiger Zustand wird über
einen ausdrücklichen Reload-Vertrag erneuert. Das ist kein echtes Assembly-Unload:
`FixWorld.Mod.dll` kann unter dem verwendeten Mono-Runtime-Modell nicht beliebig
aus dem AppDomain entfernt und ausgetauscht werden.

## Phasen

### Phase 1: Runtime-Assembly und Startvertrag

- Projekt `FixWorld.Runtime` anlegen
- den Zustandsautomaten von `NotStarted` bis `Stopped` beziehungsweise `Failed`
  als einzige Runtime-Lebenszyklusquelle implementieren
- idempotente Einstiege `StartEarly`, `AttachMod` und `Shutdown` definieren
- Loader lädt die Runtime nach erfolgreicher Vertragsprüfung genau einmal
- erfolgreicher Runtime-Start ersetzt den heutigen Loader-Claim
- absichtlich fehlende oder inkompatible Runtime lässt Vanilla ohne FixWorld-Hooks
  weiterlaufen
- noch keine bestehende Stage-Implementierung verändern

Abnahme:

- Solution-Build ohne Warnungen oder Fehler
- Contract-Tests für erlaubte und verbotene Zustandswechsel, gleichzeitige
  Startversuche, einmaligen Start, idempotentes Attach und finalen Shutdown
- absichtlich fehlende Runtime erzeugt keine Neustart-Schleife
- vollständige Modliste erreicht das Hauptmenü ohne relevante Fehler

### Phase 2: Infrastruktur und Mod-Boot früh übernehmen

- EventBus, Scheduler, Main-Thread-Dispatcher und Runtime-Lifecycle nach
  `FixWorld.Runtime` verschieben
- frühe Diagnose und Prozess-Timeline an `StartEarly()` anbinden
- bestehende Mailbox-, Stage- und Job-Verträge weiterverwenden
- `ModLoadingCoordinator` und den `LoadAllActiveMods()`-Patch aus dem Loader in
  die Runtime verschieben
- Stage-Ereignisse direkt über den Runtime-EventBus publizieren
- Loader auf Vertragsprüfung, Assembly-Load und `StartEarly()` reduzieren
- bestehende UI- und Benchmark-Telemetrie an die direkten Stage-Ereignisse hängen
- normale Mod ruft nur noch `AttachMod()` auf
- Bootstrap-Duplikate und späte Infrastrukturinitialisierung entfernen

Abnahme:

- jede Infrastruktur besitzt genau einen Prozess-Lebenszyklus
- keine Worker oder Event-Kanäle werden beim Laden der normalen Mod erneut erzeugt
- Loader enthält keine fachliche Stage-Reihenfolge und keinen Harmony-Patch auf
  RimWorld mehr
- Runtime kontrolliert `LoadAllActiveMods()` weiterhin top-level
- Mod-Reihenfolge, Hot-Reload-Entscheidungen und Fehlerverhalten entsprechen dem
  aktuellen funktionierenden Stand
- bestehende 64 Runtime-Assertions bleiben erfolgreich oder werden ohne Verlust der
  geprüften Verträge in Runtime-Tests überführt
- vollständiger 88-Mod-Lauf bleibt funktional

### Phase 3: Dünne Mod-Brücke

- Hauptassembly in `FixWorld.Mod.dll` umbenennen
- nur Modklasse, Einstellungen, UI und Attach-Brücke dort behalten
- alte `FixWorld.dll` aus Build, Paket und Modordner entfernen
- verhindern, dass alte und neue Hauptassembly gleichzeitig geladen werden
- DDS und andere modabhängige Subsysteme über einen typisierten Settings-Snapshot
  konfigurieren

Abnahme:

- RimWorld lädt `FixWorld.Runtime.dll` früh und `FixWorld.Mod.dll` später jeweils
  genau einmal
- `CreateModClasses()` erzeugt genau eine FixWorld-Modinstanz
- Einstellungen und UI funktionieren unverändert
- kein Codepfad initialisiert Scheduler, Events oder Hooks ein zweites Mal

### Phase 4: Frühe RimWorld-Stages vollständig übernehmen

Reihenfolge:

1. `InitializeMods()` und `ModContentPack`-Erzeugung
2. Assembly-Discovery und Assembly-Loading
3. `LoadModContent()` und direkte Mod-Telemetrie
4. `CreateModClasses()` und Konstruktorzeiten
5. XML-, Patch- und Definitions-Stages

Jede Stage erhält:

- typisierten Input und Output
- eindeutige Mod-Zuordnung, sofern ehrlich möglich
- Thread-Affinität und Ausführungsmodus
- geordnete Fehlerbehandlung
- einen expliziten RimWorld-Fallback nur vor der ersten Mutation
- Stage-, Worker-, Wait-, Commit- und Wall-Time

Abnahme je Stage:

- vollständige Modliste lädt ohne zusätzliche relevante Fehler
- Reihenfolge und Anzahl der aktiven Mods bleiben identisch
- unbekannte Verträge stoppen die FixWorld-Übernahme vor partieller Mutation
- alte Closure-, String- und DeepProfiler-Erkennung der übernommenen Stage wird
  entfernt

### Phase 5: Harmony instrumentieren

- aktuellen Mod- und Stage-Kontext während Modkonstruktoren und statischen
  Initialisierungen setzen
- Harmony-Owner, Zielmethode, Patch-Art, Dauer und Fehler erfassen
- Messdaten einem Mod nur bei exakter oder klar abgeleiteter Zuordnung zuschreiben
- Harmony-Ausführung weiterhin der installierten Harmony-Version überlassen
- erst nach Messung konkrete Umleitungen oder Optimierungen einzeln entscheiden

Abnahme:

- FixWorld kann die teuersten Harmony-Nutzer und Patch-Ziele berichten
- Mods beobachten weiterhin synchrone Harmony-Semantik
- Harmony-Update erfordert keinen Austausch einer mitgelieferten FixWorld-Fork

## Fehlergrenzen

- Fehler vor dem ersten FixWorld-Patch: Vanilla läuft weiter, FixWorld bleibt für
  diesen Start deaktiviert.
- Fehler nach einer partiellen Stage-Mutation: kein unehrlicher Vanilla-Neustart im
  selben Prozess. Die Stage meldet den Fehler eindeutig und bricht kontrolliert ab.
- Unbekannte RimWorld-MVID oder fehlende Signatur: keine Übernahme.
- Fremde Harmony-Patches werden gemessen. Ein Fallback bleibt nur dort bestehen,
  wo der Vertrag noch nicht vollständig FixWorld gehört.
- Keine Phase gilt als abgeschlossen, solange Build, Contract-Tests oder der reale
  Vollmodlisten-Test neue Fehler enthalten.

## Explizit später

- öffentliche API und Versionsgarantie für andere Mods
- vollständiger Harmony-Proxy oder Harmony-Fork
- Änderung der von Mods beobachteten synchronen Patch-Semantik
- neue Worker-Parallelisierung innerhalb noch nicht verstandener RimWorld-Stages
- Linux-Doorstop beziehungsweise Linux-Konverter
- DDS-Packformat, OBST und GPU-Verarbeitung

## Nächster ausführbarer Schnitt

Phase 1 ist als verhaltensgleicher Assembly-Schnitt abgeschlossen. Der Solution-Build
ist warnungsfrei, 86 Contract-Assertions sind erfolgreich, die absichtlich fehlende
Runtime fällt ohne Neustart auf Vanilla zurück und die vollständige 88-Mod-Liste
erreicht das Hauptmenü ohne relevante Fehler.

Der nächste ausführbare Schnitt ist Phase 2. Dabei wandern Infrastruktur und
Mod-Boot in die Runtime, ohne bestehende Loading-Stages parallel auszuführen oder
`InitializeMods()` bereits zu ersetzen.
