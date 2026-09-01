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
RimWorld-Assembly per MVID und den versionierten Runtime-Vertrag. Erst nachdem
`FixWorld.Runtime` den Zustand `EarlyReady` erreicht hat, übernimmt der Loader
`LoadedModManager.LoadAllActiveMods()`. Die normale FixWorld-Modinstanz bindet sich
später genau einmal an dieselbe Runtime. Unbekannte `winhttp.dll`- oder
`doorstop_config.ini`-Dateien werden nicht überschrieben.

Es gibt keine Legacy-Konfiguration, keinen optionalen Spätpfad und keinen
Enable-/Disable-Modus. Das Testscript installiert denselben produktiven Pfad über
`FixWorld.Preloader.Tool.exe install`.

Ist Doorstop nach dem Neustart zwar aktiviert, aber nicht im Prozess aktiv,
startet FixWorld RimWorld nicht erneut. Ist Doorstop aktiv, konnte
`FixWorld.Loader` die Mod-Ladepipeline aber nicht übernehmen, bleibt die normale
FixWorld-Runtime für diesen Start deaktiviert und RimWorld verwendet seinen
ursprünglichen Loader.

## DDS-Read-ahead

Der Preloader kann den vorhandenen DDS-Index und begrenzt DDS-Daten aktiver Mods in den
Windows-Dateicache lesen. Das Standardbudget ist der kleinere Wert aus 256 MiB und einem
Achtel des freien physischen RAM. `FIXWORLD_DDS_READ_AHEAD_MIB=0` deaktiviert nur diesen
Read-ahead, nicht den Preloader.

## Entfernen

RimWorld schließen und aus dem FixWorld-Modordner ausführen:

```powershell
.\Tools\Windows-x64\FixWorld.Preloader.Tool.exe uninstall
```

Das Tool entfernt ausschließlich eine über Eigentumsmarker und Doorstop-Prüfsumme
erkannte FixWorld-Installation. Linux bleibt ein späterer, separater Port.
