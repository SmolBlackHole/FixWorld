# FixWorld-Ideen

Diese Datei sammelt mögliche größere Richtungsentscheidungen. Sie ist weder Roadmap
noch TODO-Liste. Umgesetzt wird eine Idee erst, wenn Messwerte und ein konkreter Slice
sie rechtfertigen.

## Frühe Runtime statt wachsendem Mod

FixWorld kann langfristig als Laufzeit-Patchschicht aufgebaut werden. RimWorlds Dateien
bleiben unverändert; ausgewählte Methoden werden im Prozess in FixWorld umgeleitet.

```text
RimWorldWin64.exe
└─ UnityDoorstop
   └─ FixWorld.Bootstrap
      ├─ Version und Zielmethoden prüfen
      ├─ Assembly-Laden beobachten
      └─ FixWorld.Runtime einmalig laden
         ├─ Patch-Registry und Fallbacks
         ├─ Scheduler und Telemetrie
         ├─ Loader
         ├─ DDS
         └─ UI

RimWorld-Modloader
└─ FixWorld.Mod
   ├─ mit der frühen Runtime verbinden
   └─ ohne aktiven Doorstop Installation und einmaligen Neustart auslösen
```

Harmony bleibt zunächst das kompatible Patch-Backend. Doorstop liefert nur den frühen,
abhängigkeitsarmen Einstieg. Eine eigene Detour-Engine kommt erst infrage, wenn Messungen
belegen, dass relevante Arbeit vor Harmony unerreichbar bleibt. `Assembly-CSharp.dll`
wird nicht dauerhaft umgeschrieben.

Zunächst reichen drei physische Assemblies: `FixWorld.Bootstrap`, `FixWorld.Runtime` und
`FixWorld.Mod`. Loader, DDS und UI bleiben klar getrennte Runtime-Module und werden erst
bei einem konkreten Vorteil in eigene DLLs ausgelagert.

## Loading-Pipeline

- RimWorld-Actions einmalig in typisierte Work-Items übersetzen und die Klassifizierung
  pro Methodentyp cachen.
- Einen Coordinator für Reihenfolge und Fallbacks sowie einen Scheduler für Zeitbudget,
  Main-Thread- und spätere Worker-Arbeit verwenden.
- Unity-, Verse- und Harmony-Zustand ausschließlich auf dem Hauptthread verändern.
- Worker nur für reine Datei-, Hash-, Cache- und Byte-Arbeit einsetzen. Ergebnisse sind
  unveränderlich und werden geordnet auf dem Hauptthread übernommen.
- Fortschritt über günstige Snapshots beziehungsweise eine Mailbox höchstens alle
  100 bis 200 ms an die UI liefern.
- Jede Übernahme featureweise deaktivieren, wenn Version, IL-Form oder fremde Patches
  nicht unterstützt werden. Dann läuft die originale RimWorld-Action.

## Messung und spätere Experimente

- Ladezeit pro Mod als `Exact`, `Inferred` oder `Global` ausweisen.
- Hauptthreadzeit, Workerzeit, Wartezeit und Wall-Time getrennt erfassen.
- FixWorlds eigenen Aufwand für Klassifizierung, Scheduling und Telemetrie messen.
- Discovery, Read-ahead, Cache-Validierung, Hashing und DDS-Vorbereitung zuerst als
  Worker-Kandidaten prüfen.
- NVMe, SATA-SSD und HDD sowie kalte und warme Datei- und Anwendungscaches getrennt
  vergleichen.
- DDS-Packs oder OBST erst bewerten, sobald eine direkte Byte-/Stream-Grenze existiert.
- GPU-Dekodierung und Uploads erst nach einer sauberen CPU- und Main-Thread-Aufteilung
  untersuchen.
