# Harmony-Eignung und Proof of Concept

Stand: 2026-08-30

## Ergebnis

Ein externer Harmony-Mod ist fuer diesen RimWorld-Build technisch und zur
Laufzeit geeignet. Der PoC kompiliert getrennt von der Spielinstallation,
referenziert die lokalen RimWorld-/Harmony-Assemblies ohne sie zu kopieren und
enthaelt keinen Optimierungscode. Der isolierte Quicktest hat sowohl das Laden
der Mod als auch die Ausfuehrung des neutralen Postfix bestaetigt.

## Zielplattform

- Target Framework: `.NET Framework 4.7.2` (`net472`)
- Plattform: `x64`
- Managed Image Runtime der erzeugten DLL: `v4.0.30319`
- Build-SDK: Scoop `dotnet-sdk 10.0.400`
- .NET-Framework-Referenzen: projektlokales NuGet-Buildpaket
  `Microsoft.NETFramework.ReferenceAssemblies.net472 1.0.3`
- Visual Studio und Unity Editor sind nicht erforderlich.

`net472` wird zusaetzlich durch den aktuellen 1.6-Quellstand von Dubs
Performance Analyzer bestaetigt. ILSpys generiertes `net40` fuer die komplette
Spielassembly ist nur eine Rekonstruktionsvorgabe aus Assemblymetadaten und kein
geeignetes Ziel fuer einen neuen 1.6-Mod.

## Lokale Harmony-Laufzeit

Steam Workshop Item: `2009463077`

- Package ID: `brrainz.harmony`
- 1.6-LoadFolder: `Current`
- About-Modversion: `2.4.2.0`
- tatsaechliche `0Harmony.dll` Assembly-/Dateiversion: `2.4.1.0`
- SHA-256: `353DAAFEC180BB8E7BBE4DA78F2A7CDC78067392E3A4E79DC8E7AF295F2371E6`

Der PoC kompiliert bewusst gegen die tatsaechliche Runtime-DLL, nicht gegen das
abweichende About-Label oder eine weitere NuGet-Harmony-Kopie.

## PoC-Verhalten

Projekt: `mod/RimWorldOptim.Poc/`

- `RimWorldOptimPocMod` initialisiert Harmony mit der eindeutigen ID
  `local.rimworldoptim.poc`.
- Beim Laden erscheint eine klare Meldung, dass keine Optimierung aktiv ist.
- Ein reiner Postfix auf `Verse.Game.FinalizeInit()` schreibt nach dem Laden
  eines Spiels eine zweite Beweismeldung.
- Prefix/Postfix aendern weder Parameter, Rueckgabewert noch Spielzustand.
- Es gibt keinen Transpiler und keinen Ersatz fuer eine RimWorld-Methode.

## Build-Nachweis

```powershell
.\mod\build.ps1
```

- Build: erfolgreich
- Warnungen: `0`
- Fehler: `0`
- eigene Ausgabe: `RimWorldOptim.Poc.dll` und Portable PDB
- DLL-SHA-256: `51372E9496F51D3D223DBD9B5CCD77379EA0862E3A83C1FC051803E043C66DD7`
- zweiter Build mit `--no-restore`: identischer DLL-Hash

Assembly-Referenzen der PoC-DLL:

- `Assembly-CSharp, Version=1.6.9676.17735`
- `0Harmony, Version=2.4.1.0`
- `mscorlib, Version=4.0.0.0`

Im Ausgabeordner liegen keine RimWorld-, Unity- oder Harmony-DLLs.

## Laufzeitnachweis

Der ausdruecklich freigegebene, reversible Junction lautet:

```text
G:\Steam\steamapps\common\RimWorld\Mods\RimWorldOptim.Poc
  -> D:\Projects\RimworldOptim\mod\RimWorldOptim.Poc
```

Der Test startete RimWorld mit einem eigenen Datenordner, eigener
`ModsConfig.xml`, eigenem Log und `-quicktest`. Die normalen Nutzerdaten wurden
nicht verwendet. Nach der automatischen Testspiel-Erzeugung enthielt das Log:

```text
[RimWorldOptim.Poc] Loaded. No optimization patches are active.
[RimWorldOptim.Poc] Harmony PoC observed Game.FinalizeInit. Game state was not changed.
```

Es wurden keine relevanten Harmony-Patch-, Assembly-, `MissingMethod`- oder
`TypeLoad`-Fehler gefunden. Zwei Meldungen des Unity-Fallback-Handlers zu
dynamisch benannten nativen Bibliotheken traten vor dem PoC-Load auf; sie
beeintraechtigten weder Assembly-Laden noch Testspiel-Erzeugung.

Reproduzierbarer Aufruf:

```powershell
.\mod\test-load.ps1
```
