# Werkzeuge

Portable Drittanbieterwerkzeuge liegen lokal unterhalb dieses Ordners und sind
per `.gitignore` ausgeschlossen. Eigene Skripte und diese Dokumentation bleiben
versionierbar.

Verwendet wird `ilspycmd 11.0.0.9375` aus der offiziellen ILSpy-NuGet-
Distribution. Das reicht fuer eine reproduzierbare C#-Dekompilierung; die
ILSpy-GUI oder dnSpyEx sind fuer den initialen Ablauf nicht erforderlich.

Das lokale NuGet-Paket hat den SHA-256-Hash
`8F555B3FCA90A1D7A59050D78539C69DEEDAAD421756BD4FE478D135BDAC2DEA`.

Aufruf:

```powershell
.\tools\decompile.ps1 -RimWorldRoot 'G:\Steam\steamapps\common\RimWorld'
```

Das Skript prueft den erwarteten Quell-Hash, verweigert das Ueberschreiben einer
vorhandenen Ausgabe und deaktiviert ILSpys Update-Check fuer einen stabilen Lauf.
