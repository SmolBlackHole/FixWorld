# Windows-Preloader

FixWorld benötigt unter Windows x64 einen frühen Prozesseinstieg über
[UnityDoorstop 4.4.0](https://github.com/NeighTools/UnityDoorstop/releases/tag/v4.4.0).

Beim ersten Start installiert der normale Mod zwei Dateien neben
`RimWorldWin64.exe` und startet RimWorld einmal neu:

- `winhttp.dll`, unverändert aus UnityDoorstop 4.4.0
- `doorstop_config.ini`, mit FixWorld-Eigentumsmarker

Ab dem nächsten Start gilt genau ein Pfad:

```text
Doorstop -> FixWorld.Preloader -> FixWorld.Loader -> FixWorld.Runtime
                                                        |
                                                        -> ModLoadingCoordinator
```

Der Preloader lädt die DLL der installierten Harmony-Mod. `FixWorld.Loader` prüft die
RimWorld-Assembly per MVID und den versionierten Runtime-Vertrag, lädt
`FixWorld.Runtime` und ruft `StartEarly()` auf. Die Runtime richtet die langlebige
Infrastruktur ein und übernimmt danach `LoadedModManager.LoadAllActiveMods()`.
Der Loader besitzt weder einen Harmony-Patch noch eine fachliche Loading-Stage. Die
normale `FixWorld.Mod.dll` bindet sich später genau einmal an dieselbe Runtime.
Unbekannte `winhttp.dll`- oder `doorstop_config.ini`-Dateien werden nicht
überschrieben.

Seit Phase 3 ersetzt `FixWorld.Mod.dll` die frühere `FixWorld.dll`. Build und Runtime
entfernen einen eindeutig erkannten Altbestand, bevor RimWorld beide Assemblies
gleichzeitig laden kann. Das erlaubt auch ein Update, das über einen bestehenden
privaten Pilotordner kopiert wurde.

Es gibt keine Legacy-Konfiguration, keinen optionalen Spätpfad und keinen
Enable-/Disable-Modus. Das Testscript installiert denselben produktiven Pfad über
`FixWorld.Tool.exe preloader install`.

Ist Doorstop nach dem Neustart zwar aktiviert, aber nicht im Prozess aktiv,
startet FixWorld RimWorld nicht erneut. Ist Doorstop aktiv, konnte
`FixWorld.Runtime` die Mod-Ladepipeline aber nicht übernehmen, bleibt FixWorld für
diesen Start deaktiviert und RimWorld verwendet seinen ursprünglichen Loader.

## DDS-Read-ahead

Der Preloader kann den vorhandenen DDS-Index und begrenzt DDS-Daten aktiver Mods in den
Windows-Dateicache lesen. Das Standardbudget ist der kleinere Wert aus 256 MiB und einem
Achtel des freien physischen RAM. `FIXWORLD_DDS_READ_AHEAD_MIB=0` deaktiviert nur diesen
Read-ahead, nicht den Preloader.

## Entfernen

RimWorld schließen und aus dem FixWorld-Modordner ausführen:

```powershell
.\Tools\Windows-x64\FixWorld.Tool.exe preloader uninstall
```

Das Tool entfernt ausschließlich eine über Eigentumsmarker und Doorstop-Prüfsumme
erkannte FixWorld-Installation. Linux bleibt ein späterer, separater Port.
