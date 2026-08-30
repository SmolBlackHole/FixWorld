# Roadmap

## Ziel

RimWorld mit großen Modlisten und komplexen Kolonien messbar beschleunigen,
ohne Spielverhalten oder Saves zu beschädigen.

## 1. Grundlage

Der Zielbuild ist dekompiliert, FixWorld baut, ein komplexer Save ist
eingefroren und Dubs Performance Analyzer ist installiert.

## 2. Mod-Loading

Den Vanilla-Start in `Bootstrap`, `XML & Patches`, `Definitions`, `Content` und
`Finalize` messen. Danach den größten reproduzierbaren Anteil mit genau einer
kleinen Änderung per A/B-Test bewerten. Die spätere optimierte Content-Pipeline
bekommt eigene Stages und wird nicht mit der Vanilla-Reihenfolge vermischt.

## 3. Ingame-Performance

TPS des festen Saves messen, mit Dubs einen dominanten Methodenpfad bestimmen
und genau eine verhaltensneutrale Optimierung per A/B-Test bewerten.

## 4. Wiederholen

Nur nach einem klaren, reproduzierbaren Erfolg folgt der nächste Engpass.
Unklare oder unwirksame Änderungen werden vollständig zurückgebaut.

## Später

DDS-Packs, OBST als mögliches Packformat und der Linux-Konverter folgen erst,
wenn der staged Loader eine direkte Byte-/Stream-Ladegrenze besitzt.

## Grenzen

- keine Änderungen an RimWorld-DLLs
- kein breites Multithreading als erster Eingriff
- keine Veröffentlichung oder Kompatibilitätsarbeit vor einem bewiesenen Effekt
- keine Dokumentation pro Experiment, Messwerte gehören in die CSV-Dateien
