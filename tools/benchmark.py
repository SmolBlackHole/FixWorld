#!/usr/bin/env python3
from __future__ import annotations

import argparse
import csv
from datetime import datetime
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import time
from typing import Mapping, Sequence, TypedDict, cast
import xml.etree.ElementTree as ET

from build import build_mod
from rimworld_process import is_rimworld_running, launch, select_monitor


ROOT = Path(__file__).resolve().parent.parent
DEFAULT_GAME_ROOT = Path(r"G:\Steam\steamapps\common\RimWorld")
FIXTURE_ID = "spoon-spring-v1-fixworld"
FIXTURE_CONFIG = ROOT / "benchmarks" / "saves" / "spoon-spring-v1-ModsConfig.xml"
RESULT_FIELDS: tuple[str, ...] = (
    "id",
    "track",
    "variant",
    "run",
    "build",
    "fixture",
    "wall_ms",
    "total_ms",
    "texture_ms",
    "tps",
    "fps",
    "result",
    "notes",
)


class CompletionData(TypedDict):
    source: str


class LoaderStageData(TypedDict):
    number: int
    name: str
    observed: bool
    exclusiveMs: float
    mainThreadMs: float
    workerThreadMs: float


class LoaderStepData(TypedDict):
    id: str
    number: int
    stage: str
    name: str
    calls: int
    totalMs: float
    exclusiveMs: float
    mainThreadMs: float
    workerThreadMs: float


class DelayedActionData(TypedDict):
    method: str
    packageId: str
    mod: str
    calls: int
    totalMs: float
    maxMs: float


class StaticConstructorData(TypedDict):
    type: str
    packageId: str
    mod: str
    calls: int
    totalMs: float
    maxMs: float
    failures: int


class ModLoadingData(TypedDict):
    packageId: str
    mod: str
    attribution: str
    stage: str
    operation: str
    calls: int
    failures: int
    executionMs: float
    mainThreadMs: float
    workerThreadMs: float
    waitMs: float
    wallMs: float


class LoadingOverheadData(TypedDict):
    operation: str
    calls: int
    totalMs: float
    maxMs: float
    estimated: bool


class LoaderData(TypedDict):
    observedMs: float
    stages: list[LoaderStageData]
    steps: list[LoaderStepData]
    delayedActions: list[DelayedActionData]
    staticConstructors: list[StaticConstructorData]
    staticConstructorTailMs: float
    mods: list[ModLoadingData]
    overhead: list[LoadingOverheadData]


class FileData(TypedDict):
    totalMs: float


class TexturePathData(TypedDict):
    duplicatePaths: int


class TextureData(TypedDict):
    totalMs: float
    ddsMs: float


class DdsCacheData(TypedDict):
    hits: int
    misses: int
    workerCount: int
    workerPreparedMods: int
    workerAppliedMods: int
    workerFallbackMods: int


class BenchmarkReport(TypedDict):
    schemaVersion: int
    completion: CompletionData
    loader: LoaderData
    files: FileData
    texturePaths: TexturePathData
    textures: TextureData
    ddsCache: DdsCacheData


class ResultRecord(TypedDict):
    id: str
    track: str
    variant: str
    run: int
    build: str
    fixture: str
    wall_ms: int
    total_ms: str
    texture_ms: str
    tps: str
    fps: str
    result: str
    notes: str


def bounded_int(minimum: int, maximum: int):
    def parse(value: str) -> int:
        number = int(value)
        if not minimum <= number <= maximum:
            raise argparse.ArgumentTypeError(f"must be between {minimum} and {maximum}")
        return number

    return parse


def prepare_config(
    destination: Path, texture_compression: bool, use_live_mods: bool
) -> int:
    live_config = (
        Path.home()
        / "AppData"
        / "LocalLow"
        / "Ludeon Studios"
        / "RimWorld by Ludeon Studios"
        / "Config"
    )
    if not live_config.is_dir():
        raise RuntimeError(f"RimWorld configuration does not exist: {live_config}")
    if not use_live_mods and not FIXTURE_CONFIG.is_file():
        raise RuntimeError(f"Benchmark fixture does not exist: {FIXTURE_CONFIG}")

    shutil.copytree(live_config, destination, dirs_exist_ok=True)
    if not use_live_mods:
        shutil.copy2(FIXTURE_CONFIG, destination / "ModsConfig.xml")

    prefs_path = destination / "Prefs.xml"
    prefs = ET.parse(prefs_path)
    root = prefs.getroot()
    _set_xml_text(root, "logVerbose", "False")
    _set_xml_text(root, "fullscreen", "False")
    _set_xml_text(root, "textureCompression", str(texture_compression))
    prefs.write(prefs_path, encoding="utf-8", xml_declaration=True)

    mods = ET.parse(destination / "ModsConfig.xml").getroot()
    return len(mods.findall("./activeMods/li"))


def _set_xml_text(root: ET.Element, name: str, value: str) -> None:
    element = root.find(name)
    if element is None:
        element = ET.SubElement(root, name)
    element.text = value


def wait_for_report(
    process: subprocess.Popen[bytes], report_path: Path, timeout_seconds: int
) -> tuple[int, BenchmarkReport]:
    started = time.monotonic()
    while time.monotonic() - started < timeout_seconds:
        if report_path.is_file():
            with report_path.open("r", encoding="utf-8") as source:
                raw: object = json.load(source)
            return (
                round((time.monotonic() - started) * 1000),
                validate_report(raw),
            )
        if process.poll() is not None:
            raise RuntimeError("RimWorld exited before writing the benchmark report.")
        time.sleep(0.1)
    raise TimeoutError(f"RimWorld did not finish within {timeout_seconds} seconds.")


def validate_report(raw: object) -> BenchmarkReport:
    report = _string_dict(raw, "benchmark report")
    if report.get("schemaVersion") != 4:
        raise RuntimeError(
            f"Unsupported benchmark schema: {report.get('schemaVersion')!r}"
        )
    completion = _string_dict(report.get("completion"), "completion")
    loader = _string_dict(report.get("loader"), "loader")
    if completion.get("source") != "staged-runner":
        raise RuntimeError(f"Unexpected completion data: {completion!r}")
    stages = loader.get("stages")
    steps = loader.get("steps")
    delayed_actions = loader.get("delayedActions")
    static_constructors = loader.get("staticConstructors")
    static_constructor_tail = loader.get("staticConstructorTailMs")
    mods = loader.get("mods")
    overhead = loader.get("overhead")
    if (
        not isinstance(stages, list)
        or len(stages) != 5
        or not isinstance(steps, list)
        or not steps
        or not isinstance(delayed_actions, list)
        or not delayed_actions
        or not isinstance(static_constructors, list)
        or not static_constructors
        or not isinstance(static_constructor_tail, (int, float))
        or not isinstance(mods, list)
        or not isinstance(overhead, list)
    ):
        raise RuntimeError("Benchmark report contains incomplete loader measurements.")
    for section in ("files", "texturePaths", "textures", "ddsCache"):
        _string_dict(report.get(section), section)
    return cast(BenchmarkReport, report)


def _string_dict(value: object, name: str) -> dict[str, object]:
    if not isinstance(value, dict) or not all(isinstance(key, str) for key in value):
        raise RuntimeError(f"Benchmark report contains no valid {name} section.")
    return cast(dict[str, object], value)


def write_loader_csvs(run_root: Path, report: BenchmarkReport) -> None:
    loader = report["loader"]
    _write_csv(
        run_root / "loader-stages.csv",
        ("number", "name", "observed", "exclusiveMs", "mainThreadMs", "workerThreadMs"),
        loader["stages"],
    )
    _write_csv(
        run_root / "loader-steps.csv",
        (
            "id",
            "number",
            "stage",
            "name",
            "calls",
            "totalMs",
            "exclusiveMs",
            "mainThreadMs",
            "workerThreadMs",
        ),
        loader["steps"],
    )
    _write_csv(
        run_root / "delayed-actions.csv",
        ("method", "packageId", "mod", "calls", "totalMs", "maxMs"),
        loader["delayedActions"],
    )
    _write_csv(
        run_root / "static-constructors.csv",
        ("type", "packageId", "mod", "calls", "totalMs", "maxMs", "failures"),
        loader["staticConstructors"],
    )
    _write_csv(
        run_root / "loader-mods.csv",
        (
            "packageId",
            "mod",
            "attribution",
            "stage",
            "operation",
            "calls",
            "failures",
            "executionMs",
            "mainThreadMs",
            "workerThreadMs",
            "waitMs",
            "wallMs",
        ),
        loader["mods"],
    )
    _write_csv(
        run_root / "fixworld-overhead.csv",
        ("operation", "calls", "totalMs", "maxMs", "estimated"),
        loader["overhead"],
    )


def _write_csv(
    path: Path, fields: tuple[str, ...], rows: Sequence[Mapping[str, object]]
) -> None:
    with path.open("w", newline="", encoding="utf-8") as output:
        writer = csv.DictWriter[str](output, fieldnames=fields, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def relevant_error_count(log_path: Path) -> int:
    log = log_path.read_text(encoding="utf-8", errors="replace")
    patterns = (
        r"Exception while patching",
        r"Could not load file or assembly",
        r"MissingMethodException",
        r"TypeLoadException",
        r"Root level exception",
        r"\[FixWorld\].*(?:error|exception)",
    )
    return sum(len(re.findall(pattern, log, re.IGNORECASE)) for pattern in patterns)


def append_result(record: ResultRecord) -> None:
    with (ROOT / "benchmarks" / "results.csv").open(
        "a", newline="", encoding="utf-8"
    ) as output:
        csv.DictWriter[str](output, fieldnames=RESULT_FIELDS).writerow(record)


def run_once(args: argparse.Namespace, run_number: int) -> None:
    game_root = args.game_root.resolve()
    game_executable = game_root / "RimWorldWin64.exe"
    if not game_executable.is_file():
        raise RuntimeError(f"RimWorld does not exist: {game_executable}")
    if is_rimworld_running():
        raise RuntimeError("RimWorld is already running.")

    timestamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")[:-3]
    run_id = f"L4-{args.variant}-{timestamp}-r{run_number}"
    run_root = ROOT / "profiling" / "captures" / "loader" / run_id
    user_data = run_root / "userdata"
    config = user_data / "Config"
    log_path = run_root / "Player.log"
    report_path = run_root / "profile.json"
    config.mkdir(parents=True)

    active_mods = prepare_config(
        config, not args.disable_texture_compression, args.live_mods
    )
    monitor = select_monitor(args.monitor_name, args.monitor)
    cache_root = args.dds_cache_root or ROOT / "profiling" / "cache" / "dds-v1"
    if args.dds_cache:
        cache_root.mkdir(parents=True, exist_ok=True)

    environment = os.environ.copy()
    environment.update(
        {
            "FIXWORLD_BENCHMARK_OUTPUT": str(report_path),
            "FIXWORLD_DDS_CACHE": "1" if args.dds_cache else "0",
            "FIXWORLD_DDS_CACHE_ROOT": str(cache_root),
        }
    )
    if args.dds_workers is None:
        environment.pop("FIXWORLD_DDS_WORKERS", None)
    else:
        environment["FIXWORLD_DDS_WORKERS"] = str(args.dds_workers)
    arguments = [
        f"-savedatafolder={user_data}",
        "-logFile",
        str(log_path),
        "-popupwindow",
    ]

    print(
        f"Benchmark {run_number}/{args.runs}: {run_id} on "
        f"{monitor.friendly_name} ({monitor.device_name})"
    )
    rimworld = launch(
        game_executable, game_root, arguments, environment, monitor, args.minimized
    )
    try:
        wall_ms, report = wait_for_report(rimworld.process, report_path, args.timeout)
        write_loader_csvs(run_root, report)
        time.sleep(0.5)

        error_count = relevant_error_count(log_path)
        loader = report["loader"]
        textures = report["textures"]
        files = report["files"]
        paths = report["texturePaths"]
        cache = report["ddsCache"]
        top_steps = sorted(
            loader["steps"], key=lambda item: float(item["exclusiveMs"]), reverse=True
        )[:5]
        top_delayed_actions = sorted(
            loader["delayedActions"],
            key=lambda item: float(item["totalMs"]),
            reverse=True,
        )[:5]
        top_static_constructors = sorted(
            loader["staticConstructors"],
            key=lambda item: float(item["totalMs"]),
            reverse=True,
        )[:5]
        top_mod_work = sorted(
            loader["mods"], key=lambda item: float(item["wallMs"]), reverse=True
        )[:5]
        notes = "; ".join(
            (
                f"activeMods={active_mods}",
                f"ddsCache={args.dds_cache}",
                f"ddsWorkers={cache['workerCount']}",
                f"textureCompression={not args.disable_texture_compression}",
                f"monitor={monitor.friendly_name}/{rimworld.actual_monitor}",
                f"relevantErrors={error_count}",
                f"observedLoaderMs={float(loader['observedMs']):.3f}",
                f"discoveryMs={float(files['totalMs']):.3f}",
                f"textureLoadMs={float(textures['totalMs']):.3f}",
                f"ddsLoadMs={float(textures['ddsMs']):.3f}",
                f"duplicateTexturePaths={paths['duplicatePaths']}",
                f"ddsCacheHits={cache['hits']}",
                f"ddsCacheMisses={cache['misses']}",
                f"ddsWorkerPreparedMods={cache['workerPreparedMods']}",
                f"ddsWorkerAppliedMods={cache['workerAppliedMods']}",
                f"ddsWorkerFallbackMods={cache['workerFallbackMods']}",
                "staticConstructorTailMs="
                f"{float(loader['staticConstructorTailMs']):.3f}",
                "topLoaderSteps="
                + "|".join(
                    f"{step['id']}={float(step['exclusiveMs']):.3f}ms"
                    for step in top_steps
                ),
                "topDelayedActions="
                + "|".join(
                    f"{action['packageId']}:{action['method']}="
                    f"{float(action['totalMs']):.3f}ms"
                    for action in top_delayed_actions
                ),
                "topStaticConstructors="
                + "|".join(
                    f"{constructor['packageId']}:{constructor['type']}="
                    f"{float(constructor['totalMs']):.3f}ms"
                    for constructor in top_static_constructors
                ),
                "topModWork="
                + "|".join(
                    f"{work['packageId']}:{work['operation']}="
                    f"{float(work['wallMs']):.3f}ms/{work['attribution']}"
                    for work in top_mod_work
                ),
                "fixWorldOverhead="
                + "|".join(
                    f"{item['operation']}={float(item['totalMs']):.3f}ms"
                    for item in loader["overhead"]
                ),
            )
        )
        record: ResultRecord = {
            "id": run_id,
            "track": "loader",
            "variant": args.variant,
            "run": run_number,
            "build": "1.6.4871 rev591",
            "fixture": "live-config" if args.live_mods else FIXTURE_ID,
            "wall_ms": wall_ms,
            "total_ms": "",
            "texture_ms": "",
            "tps": "",
            "fps": "",
            "result": "valid" if error_count == 0 else "invalid",
            "notes": notes,
        }
        append_result(record)
        print(json.dumps(record, indent=2, ensure_ascii=False))
    finally:
        rimworld.close()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Benchmark the FixWorld loader.")
    parser.add_argument("--game-root", type=Path, default=DEFAULT_GAME_ROOT)
    parser.add_argument("--runs", type=bounded_int(1, 10), default=1)
    parser.add_argument("--variant", default="staged-loader")
    parser.add_argument("--monitor-name", default="G276HL")
    parser.add_argument("--monitor", type=bounded_int(1, 8), default=2)
    parser.add_argument("--timeout", type=bounded_int(30, 600), default=180)
    parser.add_argument("--dds-cache-root", type=Path)
    parser.add_argument(
        "--dds-workers",
        type=bounded_int(0, 32),
        help="Override DDS workers. By default FixWorld uses half the logical CPUs.",
    )
    parser.add_argument("--no-dds-cache", action="store_false", dest="dds_cache")
    parser.add_argument("--disable-texture-compression", action="store_true")
    parser.add_argument("--minimized", action="store_true")
    parser.add_argument(
        "--live-mods",
        action="store_true",
        help="Use the current RimWorld ModsConfig.xml instead of the fixture list.",
    )
    parser.add_argument("--skip-build", action="store_true")
    parser.set_defaults(dds_cache=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not args.skip_build:
        build_mod()
    for run_number in range(1, args.runs + 1):
        run_once(args, run_number)
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
