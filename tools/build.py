#!/usr/bin/env python3
from __future__ import annotations

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


def build_mod() -> Path:
    dotnet = find_dotnet_sdk()
    environment = os.environ.copy()
    environment.update(
        {"DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1"}
    )
    subprocess.run(
        [
            dotnet,
            "build",
            ROOT / "mod" / "FixWorld" / "Source" / "FixWorld.csproj",
            "--configuration",
            "Release",
            "--nologo",
        ],
        cwd=ROOT,
        env=environment,
        check=True,
    )

    assembly = ROOT / "mod" / "FixWorld" / "Assemblies" / "FixWorld.dll"
    if not assembly.is_file():
        raise RuntimeError(f"Build succeeded, but the mod DLL is missing: {assembly}")
    digest = hashlib.sha256(assembly.read_bytes()).hexdigest().upper()
    print(f"SDK: {dotnet}")
    print(f"Mod DLL: {assembly}")
    print(f"SHA-256: {digest}")
    return assembly


def main() -> int:
    build_mod()
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f"error: {error}")
        raise SystemExit(1)
