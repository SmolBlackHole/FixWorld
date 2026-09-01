#!/usr/bin/env python3
from __future__ import annotations

import argparse
from datetime import datetime
import os
from pathlib import Path
import re
import shutil
import subprocess
import time

from build import build_mod
from rimworld_process import is_rimworld_running, launch, select_monitor


ROOT = Path(__file__).resolve().parent.parent
DEFAULT_GAME_ROOT = Path(r"D:\SteamLibrary\steamapps\common\RimWorld")
ERROR_PATTERNS = (
    r"Exception while patching",
    r"Could not load file or assembly",
    r"MissingMethodException",
    r"TypeLoadException",
    r"\[FixWorld\].*(?:error|exception)",
)
FATAL_STARTUP_MARKERS = (
    "[FixWorld] The active Doorstop installation is invalid",
    "[FixWorld] Doorstop was still inactive after the automatic restart",
    "[FixWorld] The required early loader could not be installed",
)
EARLY_PIPELINE_MARKER = "[FixWorld.Loader] Running the owned mod-loading pipeline."


def bounded_int(minimum: int, maximum: int):
    def parse(value: str) -> int:
        number = int(value)
        if not minimum <= number <= maximum:
            raise argparse.ArgumentTypeError(f"must be between {minimum} and {maximum}")
        return number

    return parse


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Smoke-test the FixWorld mod loader.")
    parser.add_argument("--game-root", type=Path, default=DEFAULT_GAME_ROOT)
    parser.add_argument("--monitor-name", default="G276HL")
    parser.add_argument("--monitor", type=bounded_int(1, 16), default=2)
    parser.add_argument("--timeout", type=bounded_int(10, 600), default=180)
    parser.add_argument("--no-dds-cache", action="store_false", dest="dds_cache")
    parser.add_argument("--live-mods", action="store_true")
    parser.add_argument("--keep-running", action="store_true")
    parser.add_argument("--minimized", action="store_true")
    parser.add_argument("--skip-build", action="store_true")
    parser.set_defaults(dds_cache=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not args.skip_build:
        build_mod()

    game_root = args.game_root.resolve()
    executable = game_root / "RimWorldWin64.exe"
    config_template = ROOT / "mod" / "test-data" / "Config" / "ModsConfig.xml"
    if args.live_mods:
        config_template = (
            Path.home()
            / "AppData"
            / "LocalLow"
            / "Ludeon Studios"
            / "RimWorld by Ludeon Studios"
            / "Config"
            / "ModsConfig.xml"
        )
    mod_root = ROOT / "mod" / "FixWorld"
    mod_link = game_root / "Mods" / "FixWorld"
    if not executable.is_file():
        raise RuntimeError(f"RimWorld does not exist: {executable}")
    if not config_template.is_file():
        raise RuntimeError(f"Test configuration does not exist: {config_template}")
    try:
        same_mod = os.path.samefile(mod_link, mod_root)
    except OSError:
        same_mod = False
    if not same_mod:
        raise RuntimeError(f"The FixWorld mod junction does not point to {mod_root}.")
    if is_rimworld_running():
        raise RuntimeError("RimWorld is already running.")

    installer = mod_root / "Tools" / "Windows-x64" / "FixWorld.Preloader.Tool.exe"
    if not installer.is_file():
        raise RuntimeError(f"The FixWorld preloader installer is missing: {installer}")
    installation = subprocess.run(
        [str(installer), "install", str(game_root)],
        cwd=installer.parent,
        capture_output=True,
        text=True,
        check=False,
    )
    if installation.returncode != 0:
        detail = installation.stderr.strip() or installation.stdout.strip()
        raise RuntimeError(f"Could not install the FixWorld preloader: {detail}")

    user_data = ROOT / "profiling" / "fixworld-userdata"
    config = user_data / "Config"
    config.mkdir(parents=True, exist_ok=True)
    shutil.copy2(config_template, config / "ModsConfig.xml")
    timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    log_path = user_data / f"Player-{timestamp}.log"

    environment = os.environ.copy()
    environment.pop("FIXWORLD_BENCHMARK_OUTPUT", None)
    environment["FIXWORLD_DDS_CACHE"] = "1" if args.dds_cache else "0"
    monitor = select_monitor(args.monitor_name, args.monitor)
    arguments = [
        f"-savedatafolder={user_data}",
        "-logFile",
        str(log_path),
        "-popupwindow",
    ]

    started = time.monotonic()
    rimworld = launch(
        executable, game_root, arguments, environment, monitor, args.minimized
    )
    loaded = False
    ready = False
    early_pipeline = False
    errors = 0
    try:
        while time.monotonic() - started < args.timeout:
            if log_path.is_file():
                log = log_path.read_text(encoding="utf-8", errors="replace")
                loaded = "[FixWorld] Initialized;" in log
                ready = "[FixWorld] Main menu ready." in log
                early_pipeline = EARLY_PIPELINE_MARKER in log
                fatal_marker = next(
                    (marker for marker in FATAL_STARTUP_MARKERS if marker in log),
                    None,
                )
                if fatal_marker:
                    raise RuntimeError(
                        f"FixWorld startup failed: {fatal_marker}. Log: {log_path}"
                    )
                if ready:
                    break
            if rimworld.process.poll() is not None:
                raise RuntimeError(
                    f"RimWorld exited before the loader completed. Log: {log_path}"
                )
            time.sleep(0.25)

        if not early_pipeline or not loaded or not ready:
            raise TimeoutError(
                f"Smoke test incomplete after {args.timeout} seconds. "
                f"EarlyPipeline={early_pipeline}, Loaded={loaded}, "
                f"Ready={ready}, Log={log_path}"
            )

        log = log_path.read_text(encoding="utf-8", errors="replace")
        errors = sum(
            len(re.findall(pattern, log, re.IGNORECASE)) for pattern in ERROR_PATTERNS
        )
        if errors:
            raise RuntimeError(f"Smoke test found {errors} relevant errors: {log_path}")
    finally:
        if not args.keep_running:
            rimworld.close()

    duration = time.monotonic() - started
    print(
        f"EarlyPipeline={early_pipeline} Loaded={loaded} Ready={ready} "
        f"RelevantErrors={errors} "
        f"DurationSeconds={duration:.1f} Log={log_path}"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (
        OSError,
        RuntimeError,
        TimeoutError,
        subprocess.CalledProcessError,
    ) as error:
        print(f"error: {error}")
        raise SystemExit(1)
