# Harmony-Mod

Dieser Ordner ist ausschliesslich fuer selbst geschriebenen Mod-Code vorgesehen.
RimWorld- und Unity-DLLs werden nur ueber einen lokalen Installationspfad
referenziert und nicht hierher kopiert.

`RimWorldOptim.Poc/` ist der harmlose Proof of Concept. Er zielt auf `net472`
und `x64`, schreibt eine Startmeldung und patcht `Game.FinalizeInit()` mit einem
reinen Logging-Postfix. Er enthaelt keine Optimierung und veraendert keinen
Spielzustand.

Lokale Pfade liegen in der ignorierten `Local.Build.props`. Alternativ koennen
`RIMWORLD_ROOT` und `RIMWORLD_HARMONY_ASSEMBLY` gesetzt werden.

Build:

```powershell
.\mod\build.ps1
```

Der Build legt nur eigene Artefakte unter
`mod/RimWorldOptim.Poc/Assemblies/` ab. Er kopiert nichts in die
Steam-Installation. Der Ausgabeordner ist generiert und wird nicht versioniert.

Verifizierter Release-Build: 0 Warnungen, 0 Fehler, SHA-256
`51372E9496F51D3D223DBD9B5CCD77379EA0862E3A83C1FC051803E043C66DD7`.

Der freigegebene lokale Junction bindet den Workspace-Mod hier ein:

```text
G:\Steam\steamapps\common\RimWorld\Mods\RimWorldOptim.Poc
  -> D:\Projects\RimworldOptim\mod\RimWorldOptim.Poc
```

Isolierter Laufzeittest:

```powershell
.\mod\test-load.ps1
```

Das Skript verwendet ausschliesslich `profiling/poc-userdata/`, aktiviert dort
Harmony, Core, die installierten DLCs und den PoC, startet `-quicktest`, prueft
beide PoC-Marker sowie relevante Patch-/Assemblyfehler und beendet nur die von
ihm gestartete RimWorld-Instanz. Normale Einstellungen und Spielstaende werden
nicht gelesen oder geschrieben.
