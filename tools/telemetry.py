#!/usr/bin/env python3
# pyright: strict
"""Collect local FixWorld JSONL captures. Never launches or terminates RimWorld."""

from __future__ import annotations

import argparse
import csv
from dataclasses import dataclass
from datetime import datetime, timezone
import json
import math
from pathlib import Path
import time
from typing import Iterator, TypeGuard, TypedDict, cast

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_SOURCE = (
    Path.home()
    / "AppData/LocalLow/Ludeon Studios"
    / "RimWorld by Ludeon Studios/FixWorld/Telemetry"
)


Scalar = str | bool | int | float | None


def number(value: object) -> TypeGuard[int | float]:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return False
    try:
        return math.isfinite(value)
    except OverflowError:
        return False


def object_map(value: object) -> dict[str, object]:
    if not isinstance(value, dict):
        raise ValueError("Expected JSON object")
    entries = cast(dict[object, object], value)
    if any(not isinstance(key, str) or not key for key in entries):
        raise ValueError("Expected nonempty string keys")
    return cast(dict[str, object], entries)


def object_list(value: object) -> list[object]:
    if not isinstance(value, list):
        raise ValueError("Expected JSON array")
    return cast(list[object], value)


def text(value: object) -> str:
    if not isinstance(value, str) or not value:
        raise ValueError("Expected nonempty string")
    return value


def positive_int(value: object) -> int:
    if type(value) is not int or value <= 0:
        raise ValueError("Expected positive integer")
    return value


@dataclass(frozen=True)
class Record:
    id: str
    version: int
    generation: str
    values: dict[str, Scalar]
    counters: frozenset[str]


@dataclass(frozen=True)
class Frame:
    session: str
    pid: int
    sequence: int
    utc: str
    elapsed: float
    records: tuple[Record, ...]

    @staticmethod
    def parse(raw: object) -> Frame:
        envelope = object_map(raw)
        if positive_int(envelope.get("schemaVersion")) != 1:
            raise ValueError("Unsupported capture envelope schema")
        elapsed = envelope.get("elapsedSeconds")
        if not number(elapsed) or elapsed < 0:
            raise ValueError("Invalid elapsedSeconds")
        records: list[Record] = []
        seen: set[str] = set()
        for entry in object_list(envelope.get("records")):
            item = object_map(entry)
            identity = text(item.get("id"))
            if identity in seen:
                raise ValueError("Duplicate record identity")
            seen.add(identity)
            values: dict[str, Scalar] = {}
            for key, value in object_map(item.get("values")).items():
                if value is None or isinstance(value, (str, bool)) or number(value):
                    values[key] = value
                else:
                    raise ValueError("Values must be named finite scalars")
            counters = [text(key) for key in object_list(item.get("counters"))]
            if (any(key not in values or not number(values[key]) for key in counters)
                    or len(set(counters)) != len(counters)):
                raise ValueError("Invalid counter declaration")
            records.append(
                Record(
                    identity,
                    positive_int(item.get("schemaVersion")),
                    text(item.get("generation")),
                    values,
                    frozenset(counters),
                )
            )
        return Frame(
            text(envelope.get("session")),
            positive_int(envelope.get("processId")),
            positive_int(envelope.get("sequence")),
            text(envelope.get("utc")),
            elapsed,
            tuple(records),
        )


def frames(paths: list[Path], warnings: list[str]) -> Iterator[Frame]:
    for path in sorted(paths):
        with path.open("rb") as source:
            for line_number, line in enumerate(source, 1):
                if not line.endswith(b"\n"):
                    warnings.append(
                        f"{path.name}:{line_number}: incomplete final line ignored"
                    )
                    break
                try:
                    yield Frame.parse(json.loads(line))
                except (ValueError, UnicodeError) as error:
                    warnings.append(f"{path.name}:{line_number}: {error}")


class SessionSummary(TypedDict):
    processId: int
    samples: int
    firstSeconds: float
    lastSeconds: float
    lastSequence: int
    counterDeltas: dict[str, dict[str, int | float]]


class Summary(TypedDict):
    schemaVersion: int
    sessions: dict[str, SessionSummary]
    warnings: list[str]
    note: str


def analyze(source: Path, output: Path) -> Summary:
    output.mkdir(parents=True, exist_ok=True)
    warnings: list[str] = []
    sessions: dict[str, SessionSummary] = {}
    previous: dict[tuple[str, str], tuple[Record, float]] = {}
    with (output / "metrics.csv").open("w", encoding="utf-8", newline="") as target:
        csv_output = csv.writer(target)
        csv_output.writerow(
            (
                "session",
                "process_id",
                "sequence",
                "utc",
                "module",
                "schema",
                "generation",
                "field",
                "kind",
                "value",
                "delta",
                "interval_seconds",
            )
        )
        paths = [source] if source.is_file() else list(source.glob("*.jsonl"))
        for frame in frames(paths, warnings):
            state = sessions.setdefault(
                frame.session,
                {
                    "processId": frame.pid,
                    "samples": 0,
                    "firstSeconds": frame.elapsed,
                    "lastSeconds": frame.elapsed,
                    "lastSequence": 0,
                    "counterDeltas": {},
                },
            )
            if (
                frame.pid != state["processId"]
                or frame.sequence <= state["lastSequence"]
                or frame.elapsed < state["lastSeconds"]
            ):
                warnings.append(
                    f"{frame.session}: duplicate/out-of-order sample {frame.sequence} ignored"
                )
                continue
            if state["lastSequence"] and frame.sequence != state["lastSequence"] + 1:
                warnings.append(
                    f"{frame.session}: sequence gap before {frame.sequence}"
                )
            state["samples"] += 1
            state["lastSeconds"], state["lastSequence"] = frame.elapsed, frame.sequence
            # Absence ends the baseline, even if a producer later reappears.
            present = {record.id for record in frame.records}
            for key in list(previous):
                if key[0] == frame.session and key[1] not in present:
                    del previous[key]
            for record in frame.records:
                key = (frame.session, record.id)
                old = previous.get(key)
                # Indexed presenters may reorder their rows. Textual identity
                # changes conservatively reset this module's counter baseline.
                identity = {
                    k: v for k, v in record.values.items() if isinstance(v, str)
                }
                compatible = old is not None and (
                    old[0].generation == record.generation
                    and old[0].version == record.version
                    and old[0].counters == record.counters
                    and identity
                    == {k: v for k, v in old[0].values.items() if isinstance(v, str)}
                )
                for field, value in record.values.items():
                    delta: int | float | str = ""
                    interval: float | str = ""
                    if compatible and old is not None and field in record.counters:
                        before = old[0].values.get(field)
                        if number(before) and number(value) and value >= before:
                            delta = value - before
                            interval = frame.elapsed - old[1]
                            totals = state["counterDeltas"].setdefault(record.id, {})
                            totals[field] = totals.get(field, 0) + delta
                        else:
                            warnings.append(
                                f"{frame.session}/{record.id}/{field}: counter reset"
                            )
                    csv_output.writerow(
                        (
                            frame.session,
                            frame.pid,
                            frame.sequence,
                            frame.utc,
                            record.id,
                            record.version,
                            record.generation,
                            field,
                            "counter" if field in record.counters else "value",
                            value,
                            delta,
                            interval,
                        )
                    )
                previous[key] = (record, frame.elapsed)
    if not sessions:
        warnings.append("No complete capture samples found; no measurement result available")
    summary: Summary = {
        "schemaVersion": 1,
        "sessions": sessions,
        "warnings": warnings,
        "note": "Counter deltas only; inclusive timings must not be summed into tick time."
        " Intervals use export time, not a globally atomic provider publication.",
    }
    (output / "summary.json").write_text(
        json.dumps(summary, indent=2, ensure_ascii=False), encoding="utf-8"
    )
    return summary


class Tail:
    """Copy complete lines only; reread a partial line on the next poll."""

    def __init__(self) -> None:
        self.offset = 0
        self.prefix = b""

    def copy(self, source: Path, target: Path) -> None:
        if not source.is_file():
            return
        with source.open("rb") as reader:
            prefix = reader.read(len(self.prefix))
            if source.stat().st_size < self.offset or prefix != self.prefix:
                self.offset = (
                    0  # Log rotated or replaced; preserve earlier archived bytes.
                )
                self.prefix = b""
            reader.seek(self.offset)
            with target.open("ab") as writer:
                while True:
                    line = reader.readline()
                    if not line.endswith(b"\n"):
                        break
                    writer.write(line)
                    self.offset = reader.tell()
            reader.seek(0)
            self.prefix = reader.read(min(64, self.offset))


def collect(
    source: Path, output: Path, seconds: float | None, logs: list[Path]
) -> None:
    output.mkdir(parents=True, exist_ok=False)
    raw = output / "raw"
    raw.mkdir()
    existing = set(source.glob("*.jsonl"))
    selected: set[Path] = (
        {max(existing, key=lambda path: path.stat().st_mtime)} if existing else set()
    )
    tails: dict[Path, Tail] = {}
    log_tails = [
        (path, Tail(), output / f"log-{index}-{path.name}")
        for index, path in enumerate(logs)
    ]
    start = time.monotonic()
    print(f"Collecting locally into {output}. Ctrl+C stops capture, not RimWorld.")
    try:
        while True:
            discovered = set(source.glob("*.jsonl"))
            selected.update(discovered - existing)
            existing.update(discovered)
            for path in selected:
                tails.setdefault(path, Tail()).copy(path, raw / path.name)
            for path, tail, target in log_tails:
                tail.copy(path, target)
            if seconds is not None and time.monotonic() - start >= seconds:
                break
            time.sleep(0.5)
    except KeyboardInterrupt:
        pass
    finally:
        result = analyze(raw, output)
        print(
            f"{len(result['sessions'])} session(s), {len(result['warnings'])} warning(s)."
        )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    capture = commands.add_parser(
        "collect", help="Follow newest session and subsequent restarts"
    )
    capture.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    capture.add_argument("--output", type=Path, default=None)
    capture.add_argument("--seconds", type=float)
    capture.add_argument("--log", type=Path, action="append", default=[])
    capture.add_argument(
        "--game-root", type=Path, help="Also capture FixWorld.Bootstrap.log"
    )
    report = commands.add_parser(
        "analyze", help="Analyze archived JSONL without a running game"
    )
    report.add_argument("source", type=Path)
    report.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "analyze":
        if not args.source.exists():
            parser.error("Source does not exist")
        summary = analyze(args.source, args.output)
        print(json.dumps(summary, indent=2, ensure_ascii=False))
        return 1 if summary["warnings"] else 0
    if args.seconds is not None and (
        not math.isfinite(args.seconds) or args.seconds <= 0
    ):
        parser.error("--seconds must be finite and positive")
    output = args.output or (
        ROOT
        / "data/profiling/captures/runtime"
        / datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S-%f")
    )
    logs = args.log or [args.source.parent.parent / "Player.log"]
    if args.game_root:
        logs.append(args.game_root / "FixWorld.Bootstrap.log")
    collect(args.source, output, args.seconds, logs)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError) as error:
        print(f"error: {error}")
        raise SystemExit(1)
