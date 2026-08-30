# Harmony-Mod

Dieser Ordner ist ausschließlich für selbst geschriebenen Mod-Code vorgesehen.
RimWorld- und Unity-DLLs werden nur über einen lokalen Installationspfad
referenziert und nicht hierher kopiert.

`RimWorldOptim.Poc/` ist der harmlose Proof of Concept. Er zielt auf `net472`
und `x64`, schreibt eine Startmeldung und patcht `Game.FinalizeInit()` mit einem
reinen Logging-Postfix. Er enthält keine Optimierung und verändert keinen
Spielzustand.

Im Dev-Mode stehen unter `RimWorldOptim` zwei einmalige Fixture-Aktionen bereit:

- `Create catalog control (fresh map)` erzeugt RimWorlds eingebaute Vollkolonie
  ausschließlich auf einer unbenutzten 250x250-Quicktest-Karte. Die Aktion
  akzeptiert die unveränderten Quicktest-Start-Pawns, verweigert aber zusätzliche
  Spieler-Pawns, Spielergebäude, Zonen oder Designierungen, weil der
  Vanilla-Generator vorhandenen Zustand löschen würde.
- `Report fixture activity` schreibt eine Momentaufnahme von Pawns, Jobs,
  Zeitplänen, Bills, Reservations, Türen, Zonen, Pflanzen, Stromnetzen und
  DLC-Status nach `Player.log`.

Beide Aktionen laufen nur auf ausdrücklichen Klick. Sie registrieren keine
Tick-, Update- oder Map-Hooks.

Erster Prüfablauf:

1. einen frischen 250x250-Quicktest starten,
2. `Create catalog control (fresh map)` ausführen,
3. die Simulation kontrolliert anlaufen lassen,
4. `Report fixture activity` ausführen und `Player.log` sichern.

Lokale Pfade liegen in der ignorierten `Local.Build.props`. Alternativ können
`RIMWORLD_ROOT` und `RIMWORLD_HARMONY_ASSEMBLY` gesetzt werden.

Build:

```powershell
.\mod\build.ps1
```

Der Build legt nur eigene Artefakte unter
`mod/RimWorldOptim.Poc/Assemblies/` ab. Er kopiert nichts in die
Steam-Installation. Der Ausgabeordner ist generiert und wird nicht versioniert.

Verifizierter Release-Build: 0 Warnungen, 0 Fehler, SHA-256
`D6E33C10A52EAD4DC1F999DCCAF17AE42BA3F3A632C2B0FE23CDD2A47F54B3C8`.

Der freigegebene lokale Junction bindet den Workspace-Mod hier ein:

```text
G:\Steam\steamapps\common\RimWorld\Mods\RimWorldOptim.Poc
  -> D:\Projects\RimworldOptim\mod\RimWorldOptim.Poc
```

Isolierter Laufzeittest:

```powershell
.\mod\test-load.ps1
```

Das Skript verwendet ausschließlich `profiling/poc-userdata/`, aktiviert dort
Harmony, Core, die installierten DLCs und den PoC, startet `-quicktest`, prüft
beide PoC-Marker sowie relevante Patch-/Assemblyfehler und beendet nur die von
ihm gestartete RimWorld-Instanz. Normale Einstellungen und Spielstände werden
nicht gelesen oder geschrieben.
