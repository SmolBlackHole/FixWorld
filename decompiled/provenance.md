# Decompiled reference provenance

Parent: [Decompiled reference policy](README.md)

Captured on 2026-08-30.

## Source

- Input: `<RimWorldRoot>\RimWorldWin64_Data\Managed\Assembly-CSharp.dll`
- SHA-256: `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`
- Assembly identity: `Assembly-CSharp, Version=1.6.9676.17735, Culture=neutral, PublicKeyToken=null`
- Image runtime: `v4.0.30319`
- Build reported by `RimWorld.VersionControl`: `1.6.4871 rev591`
- Installed `Version.txt`: `1.6.4871 rev590`

## Tool

- Decompiler: `ilspycmd 11.0.0.9375`
- Engine: `ICSharpCode.Decompiler 11.0.0.9375`
- Source: official NuGet package `ilspycmd 11.0.0.9375`
- Package SHA-256: `8F555B3FCA90A1D7A59050D78539C69DEEDAAD421756BD4FE478D135BDAC2DEA`
- Tool runtime: `.NET 10.0.9 x64`

## Reproduction

```text
dotnet ilspycmd.dll --disable-updatecheck --nested-directories --project \
  --outputdir decompiled\Assembly-CSharp \
  --referencepath <RimWorldRoot>\RimWorldWin64_Data\Managed \
  <RimWorldRoot>\RimWorldWin64_Data\Managed\Assembly-CSharp.dll
```

## Result

- Exit code: `0`
- Files: 9,218, including 9,217 C# files and one generated project
- Size: 28,206,111 bytes
- Namespace and nested-type layout was preserved
- ILSpy emitted no failed-decompilation markers
- No automated edits were applied to the generated code

The generated project targets `net40`. That is ILSpy's reconstruction from
assembly metadata, not evidence that a new RimWorld 1.6 mod should target
`net40`. FixWorld's actual target is documented in the
[development guide](../docs/development.md).
