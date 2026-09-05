#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import os
from pathlib import Path
import subprocess
import zipfile


ROOT = Path(__file__).resolve().parent.parent
MOD = ROOT / "mod" / "FixWorld"
OUTPUT = ROOT / "temp" / "build"


def find_dotnet_sdk() -> Path:
    executable_name = "dotnet.exe" if os.name == "nt" else "dotnet"
    candidates: list[Path] = []
    dotnet_root = os.environ.get("DOTNET_ROOT")
    if dotnet_root:
        candidates.append(Path(dotnet_root) / executable_name)
    candidates.extend(
        Path(directory.strip('"')) / executable_name
        for directory in os.environ.get("PATH", "").split(os.pathsep)
        if directory
    )

    checked: set[str] = set()
    for candidate in candidates:
        key = str(candidate).casefold()
        if key in checked or not candidate.is_file():
            continue
        checked.add(key)
        result = subprocess.run(
            [candidate, "--list-sdks"],
            capture_output=True,
            text=True,
            timeout=10,
            check=False,
        )
        if result.returncode == 0 and result.stdout.strip():
            return candidate

    raise RuntimeError("No usable .NET SDK was found on PATH or in DOTNET_ROOT.")


def run_build(project: Path, properties: tuple[str, ...] = ()) -> None:
    dotnet = find_dotnet_sdk()
    environment = os.environ.copy()
    environment.update({"DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1"})
    command: list[str | Path] = [
        dotnet,
        "build",
        project,
        "--configuration",
        "Release",
        "--nologo",
    ]
    command.extend(properties)
    subprocess.run(
        command,
        cwd=ROOT,
        env=environment,
        check=True,
    )
    print(f"SDK: {dotnet}")


def build_mod(game_root: Path | None = None, harmony: Path | None = None) -> Path:
    properties = [f"-p:OutputPath={OUTPUT.as_posix()}/",
                  f"-p:DocumentationFile={(OUTPUT / 'FixWorld.xml').as_posix()}"]
    if game_root is not None:
        managed = game_root.resolve() / "RimWorldWin64_Data" / "Managed"
        if not (managed / "Assembly-CSharp.dll").is_file():
            raise RuntimeError(f"Game references missing: {managed}")
        properties.append(f"-p:RimWorldManagedPath={managed.as_posix()}")
    if harmony is not None:
        if not harmony.is_file():
            raise RuntimeError(f"Harmony assembly missing: {harmony}")
        properties.append(f"-p:HarmonyAssemblyPath={harmony.resolve().as_posix()}")
    run_build(MOD / "FixWorld.csproj", tuple(properties))
    assembly = OUTPUT / "FixWorld.dll"
    if not assembly.is_file():
        raise RuntimeError(f"Build succeeded, but the mod DLL is missing: {assembly}")
    digest = hashlib.sha256(assembly.read_bytes()).hexdigest().upper()
    print(f"Mod DLL: {assembly}")
    print(f"SHA-256: {digest}")
    return assembly


def package_files(content: Path, output: Path) -> dict[str, Path]:
    """Explicit distribution boundary: assets and our binaries, never references."""
    files: dict[str, Path] = {}
    for directory in ("About", "Defs", "Languages", "Textures", "Tools/Doorstop-4.4.0"):
        for path in sorted((content / directory).rglob("*")):
            if path.is_file() and path.suffix.lower() in {
                ".xml", ".png", ".jpg", ".jpeg", ".txt", ".json"
            }:
                files[path.relative_to(content).as_posix()] = path
    for name in ("LoadFolders.xml", "Tools/Doorstop-4.4.0/winhttp.dll",
                 "Tools/Windows-x64/texconv.exe", "Tools/DirectXTex-LICENSE.txt"):
        files[name] = content / name
    for name in ("FixWorld.dll", "FixWorld.Bootstrap.dll"):
        files[f"v1.6/Assemblies/{name}"] = output / name
    for name in ("FixWorld.Restart.exe", "FixWorld.Restart.exe.config"):
        files[f"Tools/{name}"] = output / name
    files["LICENSE"] = ROOT / "LICENSE"
    files["THIRD_PARTY_NOTICES.md"] = ROOT / "THIRD_PARTY_NOTICES.md"
    files["HugsLib-NOTICE.txt"] = MOD / "NOTICE.txt"
    files["HugsLib-license.txt"] = MOD / "license.txt"
    for name, path in files.items():
        if not path.is_file():
            raise RuntimeError(f"Required package file missing: {name} ({path})")
    if "About/About.xml" not in files:
        raise RuntimeError("Required package metadata missing: About/About.xml")
    return files


def package_mod() -> Path:
    files = package_files(MOD / "Mods" / "FixWorld", OUTPUT)
    package = ROOT / "dist" / "FixWorld-pilot-win-x64.zip"
    package.parent.mkdir(parents=True, exist_ok=True)
    temporary = package.with_suffix(".zip.tmp")
    with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, path in sorted(files.items()):
            archive.write(path, "FixWorld/" + name)
    temporary.replace(package)
    digest = hashlib.sha256(package.read_bytes()).hexdigest().upper()
    print(f"Package: {package}")
    print(f"SHA-256: {digest}")
    return package


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Build the FixWorld mod.")
    parser.add_argument("--game-root", type=Path, default=os.environ.get("RIMWORLD_ROOT"))
    parser.add_argument("--harmony", type=Path, default=os.environ.get("RIMWORLD_HARMONY_ASSEMBLY"))
    parser.add_argument(
        "--package",
        action="store_true",
        help="create the runtime-only Windows pilot ZIP",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    build_mod(args.game_root, args.harmony)
    if args.package:
        package_mod()
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f"error: {error}")
        raise SystemExit(1)
