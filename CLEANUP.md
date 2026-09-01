# FixWorld Cleanup

Status: **aktiv und freigegeben**

## Ziel

FixWorld erhält klare Besitzer für Bootstrap, Lifecycle, Scheduling, Loading und Caching. Bestehende Primitive werden konsequent genutzt, unnötige Ebenen und ungenutzte Verträge werden entfernt. Das Verhalten der aktuellen Loading-Pipeline bleibt erhalten, solange eine Phase keine ausdrückliche Verhaltensänderung nennt.

## Leitplanken

- Interne FixWorld-APIs werden direkt umgestellt. Es entstehen keine Übergangs- oder Kompatibilitätswrapper.
- Sichere Fallbacks auf originales RimWorld- oder Harmony-Verhalten bleiben erhalten.
- Event Bus transportiert Benachrichtigungen. Der Main-Thread-Dispatcher transportiert Befehle. Beide werden nicht vermischt.
- Scheduler und Loading-Stages bleiben getrennt: Der Scheduler verwaltet Jobs und Ressourcen, die Stage-Pipeline verwaltet Reihenfolge und Barrieren.
- Generische Primitive werden nur erweitert, wenn ein aktueller Nutzer dadurch nachweislich Verantwortung abgibt.
- Keine neue generische `FileHelper`-Sammlung und keine spekulativen Backends, Policies oder Serializer-Abstraktionen.
- XML-Parallelisierung, vollständige Mod-Boot-Übernahme und neue Performance-Experimente sind nicht Teil dieses Cleanups.

## Phase 1: Bootstrap, Shutdown und Main-Thread-Dispatcher

- [x] Bootstrap erhält einen symmetrischen, idempotenten Shutdown-Pfad.
- [x] Scheduler-Shutdown verhindert neue Worker und beendet laufende Worker vor der Freigabe gemeinsam genutzter Ressourcen.
- [x] DDS-Store und zugehörige Handles werden beim normalen Shutdown freigegeben.
- [x] Der Dispatcher meldet jede fehlgeschlagene Main-Thread-Aktion zentral.
- [x] Ungenutzte Handle-, Key- und Deduplizierungs-Semantik des Dispatchers wird entfernt.

Abnahme:

- Wiederholter Shutdown bleibt sicher.
- Nach Shutdown kann kein Scheduler neu entstehen.
- Keine Main-Thread-Ausnahme verschwindet still.
- Contract-Tests und Solution-Build sind erfolgreich.

## Phase 2: Loading-Verträge und lineare Stage-Ausführung

- [x] `LoadingExecutionMode` enthält nur tatsächlich unterstützte Modi.
- [x] Loading-Pläne werden als geordnete Stage-Liste ausgeführt. Der ungenutzte DAG und dessen Topological-Sort entfallen.
- [x] Ungelesene Stage-Event-, Work-Item- und Fortschrittsfelder entfallen.
- [x] Unbenutzte Coordinator-Zähler und reine Abschlusslogs entfallen.
- [x] Thread-Affinity, Stage-Barrieren, Fallback-Actions und `ParallelThenCommit` bleiben erhalten.

Abnahme:

- Content und Finalization behalten ihre beobachtbare Reihenfolge.
- Unbekannte oder inkompatibel gepatchte Actions laufen weiterhin über das Original.
- Contract-Tests und Solution-Build sind erfolgreich.

## Phase 3: Integration und Dateigrenzen

- [x] Harmony-Patch-Inspektion wird in einem kleinen Integration-Helper zentralisiert.
- [x] Domänenspezifische Kompatibilitätsentscheidungen bleiben bei den jeweiligen Loading-Adaptern.
- [x] `ModFileLoader` übernimmt nur Dateierkennung. DDS-Anwendung und Benchmark-Beobachtung liegen beim Hook beziehungsweise Coordinator.
- [x] Atomisches Schreiben wird als fokussierte Datei-Primitive zentralisiert und von bestehenden Schreibern genutzt.

Abnahme:

- Keine doppelte Harmony-Owner-Ermittlung in den betroffenen Pfaden.
- `ModFileLoader` hängt nicht von `FixWorld.Textures` ab.
- Atomische Ersetzung und bestehende Dateiformate bleiben erhalten.
- Contract-Tests und Solution-Build sind erfolgreich.

## Phase 4: Minimaler generischer Cache-Core

- [x] Der Cache-Core besteht aus immutable Snapshot, genau einem Writer und kontrollierter Veröffentlichung.
- [x] Der Writer besitzt die Pending-Overlay-Semantik, die `TextureCacheStore` derzeit selbst implementiert.
- [x] Ungenutzte Zustände und Metadaten wie `Stale`, `Failed`, `Generation` und `Count` entfallen, sofern kein aktueller Call-Site sie benötigt.
- [x] Disk-Format, Artefaktprüfung, Eviction und DDS-Policy bleiben Eigentum von `TextureCacheStore`.

Abnahme:

- Leser sehen ausschließlich veröffentlichte immutable Snapshots.
- Der Writer kann Upsert, Remove, Lookup und Publish ohne vollständige Kopie pro Einzeländerung.
- Cache-Contract-Tests und Solution-Build sind erfolgreich.

## Phase 5: DDS-Verantwortung und Abschluss

- [ ] Der große partielle statische DDS-Zustand wird auf klare Besitzer reduziert: Lifecycle-Fassade, Store, Validierung/Planung, Builder und Background-Orchestrierung.
- [ ] Wiederholte DDS-Pfad- und Fingerprint-Logik wird nur innerhalb der Texturdomäne zentralisiert.
- [ ] Ungenutzte Overloads, Felder und Konfigurationspfade entfallen.
- [ ] Background-Jobs greifen nach Shutdown nicht auf freigegebene Ressourcen zu.
- [ ] `TODO.md` enthält nur bewusst verschobene Arbeit, nicht erledigte Cleanup-Punkte.

Abnahme:

- DDS-Hit, Miss, Invalidierung, Deferred Build und Cleanup behalten ihr Verhalten.
- Keine einzelne Klasse besitzt gleichzeitig Lifecycle, Index, Konvertierung, Telemetrie und Scheduling.
- Vollständige Solution baut ohne Warnungen oder Fehler.
- Contract-Tests bestehen.
- Ein Full-Mod-List-Smoke-Test erreicht das Hauptmenü ohne neue relevante Fehler. Er startet keinen Save.

## Bewusst später

- Frühe Übernahme von `LoadedModManager.LoadAllActiveMods()` über Doorstop und Bridge.
- Eigene vollständige Mod-Loading-Runtime und schrittweise Ablösung von Vanilla-Schritten.
- Weitere Worker-Experimente für XML, Strings, Audio, Reflection und statische Konstruktoren.
- Laufzeitoptimierungen wie Pathfinding, Path-Reuse und Tick-Scheduling.
- Containerformat- und Linux-Konverter-Experimente.

## Stop-Bedingungen

Die Umsetzung stoppt für eine neue Entscheidung, wenn ein Cleanup-Schritt das öffentliche Mod-Verhalten, das Cache-Diskformat, die Mod-Kompatibilität oder die Vanilla-Fallback-Semantik ändern müsste. Ein reiner Implementierungsfehler innerhalb der obigen Grenzen wird direkt repariert.
