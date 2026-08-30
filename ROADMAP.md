# Roadmap

## Ziel

RimWorld mit großen Modlisten und komplexen Kolonien messbar beschleunigen,
ohne Spielverhalten oder Saves zu beschädigen.

## 1. Grundlage

Der Zielbuild ist dekompiliert, der Harmony-PoC baut, ein komplexer Save ist
eingefroren und Dubs Performance Analyzer ist installiert.

## 2. Mod-Loading

Startzeit in grobe Phasen zerlegen, den größten reproduzierbaren Anteil finden
und genau eine kleine Änderung per A/B-Test bewerten.

## 3. Ingame-Performance

TPS des festen Saves messen, mit Dubs einen dominanten Methodenpfad bestimmen
und genau eine verhaltensneutrale Optimierung per A/B-Test bewerten.

## 4. Wiederholen

Nur nach einem klaren, reproduzierbaren Erfolg folgt der nächste Engpass.
Unklare oder unwirksame Änderungen werden vollständig zurückgebaut.

## Grenzen

- keine Änderungen an RimWorld-DLLs
- kein breites Multithreading als erster Eingriff
- keine Veröffentlichung oder Kompatibilitätsarbeit vor einem bewiesenen Effekt
- keine Dokumentation pro Experiment, Messwerte gehören in die CSV-Dateien
