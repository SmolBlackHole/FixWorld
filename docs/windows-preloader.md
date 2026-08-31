# Optionaler Windows-Preloader

Der normale FixWorld-Mod funktioniert ohne native Installation. Optional kann FixWorld
[UnityDoorstop 4.4.0](https://github.com/NeighTools/UnityDoorstop/releases/tag/v4.4.0)
als frühen Prozesseinstieg nutzen. Die kleine Preloader-DLL markiert dabei nur den
Einstieg. Sie lädt weder FixWorlds Haupt-DLL noch RimWorld oder Harmony. Frühe Hooks
kommen erst hinzu, wenn ihr RimWorld-Zeitpunkt einzeln untersucht und freigegeben wurde.

FixWorlds Haupt-DLL registriert ihre normalen Harmony-Patches über einen
Modulinitialisierer, sobald RimWorld sie im regulären Mod-Loader lädt.

## Aktivierung

Beim ersten Start zeigt FixWorld einmalig einen Dialog. Erst nach **Enable next launch**
werden neben `RimWorldWin64.exe` zwei Dateien angelegt:

- `winhttp.dll`, unverändert aus UnityDoorstop 4.4.0
- `doorstop_config.ini`, mit FixWorld-Eigentumsmarker

`doorstop_config.ini` verweist auf die mit dem Mod gebündelte
`Tools/Windows-x64/FixWorld.Preloader.dll`. Dadurch verwendet der nächste Start nach
einem Mod-Update automatisch den neuen Preloader, ohne eine geladene DLL ersetzen zu
müssen.

Vorhandene unbekannte `winhttp.dll`- oder Doorstop-Dateien werden niemals
überschrieben. Der Preloader wird erst beim nächsten Spielstart aktiv. Fehlt eine
gebündelte Datei oder schlägt der frühe Einstieg fehl, protokolliert der Preloader den
Fehler in `FixWorld.Preloader.log`. Der normale Mod bleibt davon unabhängig.

## Prüfsummen und Lizenz

- Release-ZIP SHA-256: `C5C06EFC2719D0853A6AA4838A28A8EAD5FFE2B8E3FB366FD579EC5134FF89DD`
- Windows-x64-`winhttp.dll` SHA-256: `93406D0A02E7C164B89828CBFE3B289930A112D2ECA50BD4A52E72ECE169E6A8`
- Lizenz: LGPL-2.1, mitgeliefert unter `Tools/Windows-x64/Doorstop-4.4.0/`

## Abschalten und Entfernen

In den FixWorld-Einstellungen lässt sich der Preloader für den nächsten Start
deaktivieren. Für die vollständige Entfernung RimWorld schließen und aus dem
FixWorld-Modordner ausführen:

```powershell
.\Tools\Windows-x64\FixWorld.Preloader.Tool.exe uninstall
```

Das Tool entfernt nur eine vollständige, über Marker und Doorstop-Prüfsumme erkannte
FixWorld-Installation. Bei fremden oder veränderten Dateien bricht es ohne Änderung ab.

Der Preloader ist vorerst ausschließlich für Windows x64 vorgesehen. Linux bleibt ein
späterer, separater Port.
