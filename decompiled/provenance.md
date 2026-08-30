# Dekompilierungs-Provenienz

Stand: 2026-08-30

## Quelle

- DLL: `G:\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll`
- SHA-256: `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`
- Assembly-Identitaet: `Assembly-CSharp, Version=1.6.9676.17735, Culture=neutral, PublicKeyToken=null`
- Image Runtime: `v4.0.30319`
- Von `RimWorld.VersionControl` berechneter Build: `1.6.4871 rev591`
- `Version.txt` der Installation: `1.6.4871 rev590`

## Werkzeug

- Decompiler: `ilspycmd 11.0.0.9375`
- Engine: `ICSharpCode.Decompiler 11.0.0.9375`
- Quelle: offizielles NuGet-Paket `ilspycmd 11.0.0.9375`
- Paket-SHA-256: `8F555B3FCA90A1D7A59050D78539C69DEEDAAD421756BD4FE478D135BDAC2DEA`
- Runtime fuer das Werkzeug: `.NET 10.0.9 x64`

## Reproduktion

```powershell
.\tools\decompile.ps1 -RimWorldRoot 'G:\Steam\steamapps\common\RimWorld'
```

Das Skript fuehrt sinngemaess aus:

```text
dotnet ilspycmd.dll --disable-updatecheck --nested-directories --project \
  --outputdir decompiled\Assembly-CSharp \
  --referencepath G:\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed \
  G:\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll
```

## Ergebnis

- Exitcode: `0`
- Dateien: `9.218`, davon `9.217` C#-Dateien und ein generiertes Projekt
- Groesse: `28.206.111` Byte
- Namespaces und Typen wurden in verschachtelten Ordnern erhalten.
- Die Ausgabe enthaelt keine von ILSpy erzeugten Marker fuer fehlgeschlagene Dekompilierung.
- Es wurden keine automatischen Korrekturen am dekompilierten Code vorgenommen.

Das generierte `Assembly-CSharp.csproj` nennt `net40`. Das ist ILSpys aus den
Metadaten abgeleitete Rekonstruktionsvorgabe, kein belastbarer Beleg dafuer, dass
ein neuer RimWorld-1.6-Mod ebenfalls `net40` anvisieren sollte.
