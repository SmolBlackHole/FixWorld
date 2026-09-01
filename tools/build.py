#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import os
from pathlib import Path
import subprocess


ROOT = Path(__file__).resolve().parent.parent


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


def run_build(project: Path, target: str | None = None) -> None:
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
    if target:
        command.append(f"-target:{target}")
    subprocess.run(
        command,
        cwd=ROOT,
        env=environment,
        check=True,
    )
    print(f"SDK: {dotnet}")


def build_mod() -> Path:
    build_runtime_components()
    run_build(ROOT / "mod" / "FixWorld" / "Source" / "Mod" / "FixWorld.Mod.csproj")

    assembly = ROOT / "mod" / "FixWorld" / "Assemblies" / "FixWorld.Mod.dll"
    if not assembly.is_file():
        raise RuntimeError(f"Build succeeded, but the mod DLL is missing: {assembly}")
    digest = hashlib.sha256(assembly.read_bytes()).hexdigest().upper()
    print(f"Mod DLL: {assembly}")
    print(f"SHA-256: {digest}")
    return assembly


def package_mod() -> Path:
    build_runtime_components()
    run_build(
        ROOT / "mod" / "FixWorld" / "Source" / "Mod" / "FixWorld.Mod.csproj",
        "Package",
    )
    package = ROOT / "dist" / "FixWorld-pilot-win-x64.zip"
    if not package.is_file():
        raise RuntimeError(f"Package target did not create the expected ZIP: {package}")
    digest = hashlib.sha256(package.read_bytes()).hexdigest().upper()
    print(f"Package: {package}")
    print(f"SHA-256: {digest}")
    return package


def build_runtime_components() -> None:
    run_build(ROOT / "mod" / "FixWorld" / "Source" / "Runtime" / "FixWorld.Runtime.csproj")
    run_build(ROOT / "mod" / "FixWorld" / "Source" / "Loader" / "FixWorld.Loader.csproj")
    run_build(ROOT / "mod" / "FixWorld" / "Source" / "Preloader" / "FixWorld.Preloader.csproj")
    run_build(
        ROOT
        / "mod"
        / "FixWorld"
        / "Source"
        / "Preloader.Tool"
        / "FixWorld.Preloader.Tool.csproj"
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Build the FixWorld mod.")
    parser.add_argument(
        "--package",
        action="store_true",
        help="create the runtime-only Windows pilot ZIP",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.package:
        package_mod()
    else:
        build_mod()
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f"error: {error}")
        raise SystemExit(1)
