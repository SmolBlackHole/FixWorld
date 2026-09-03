#!/usr/bin/env python3
"""Run public repository checks and assembly-neutral contract tests."""

from __future__ import annotations

import os
from pathlib import Path
import py_compile
import subprocess

from build import find_dotnet_sdk
from repository_checks import run_repository_checks


ROOT = Path(__file__).resolve().parent.parent


def check_python() -> None:
    for path in sorted((ROOT / "tools").glob("*.py")):
        py_compile.compile(str(path), doraise=True)
    print("Python syntax checks passed.")


def check_shared_contracts() -> None:
    dotnet = find_dotnet_sdk()
    environment = os.environ.copy()
    environment.update({"DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1"})
    subprocess.run(
        (
            dotnet,
            "run",
            "--project",
            ROOT
            / "mod"
            / "FixWorld"
            / "Tests"
            / "Shared.Contracts"
            / "FixWorld.Shared.Contracts.csproj",
            "--configuration",
            "Release",
        ),
        cwd=ROOT,
        env=environment,
        check=True,
    )


def main() -> int:
    run_repository_checks(ROOT)
    check_python()
    check_shared_contracts()
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f"error: {error}")
        raise SystemExit(1)
