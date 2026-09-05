"""Engine-free collector contracts: python -m unittest discover -s tools -p test_telemetry.py"""

import csv
import json
from pathlib import Path
import tempfile
import threading
import unittest

from telemetry import Frame, Tail, analyze, collect


def sample(
    sequence=1, count=10, session="one", generation="a", version=1, elapsed=None
):
    return {
        "schemaVersion": 1,
        "session": session,
        "processId": 123,
        "sequence": sequence,
        "utc": "2026-09-05T00:00:00Z",
        "elapsedSeconds": sequence if elapsed is None else elapsed,
        "records": [
            {
                "id": "new.module",
                "schemaVersion": version,
                "generation": generation,
                "values": {"requests": count, "gauge": 15, "identity": "row"},
                "counters": ["requests"],
            }
        ],
    }


class Contracts(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def report(self, samples):
        source = self.root / "data.jsonl"
        source.write_text(
            "".join(json.dumps(s) + "\n" for s in samples), encoding="utf-8"
        )
        return analyze(source, self.root / "report")

    def test_generic_contract_and_deltas(self):
        result = self.report([sample(), sample(2, 17)])
        self.assertEqual(
            result["sessions"]["one"]["counterDeltas"], {"new.module": {"requests": 7}}
        )
        with (self.root / "report/metrics.csv").open(encoding="utf-8") as source:
            rows = list(csv.DictReader(source))
        self.assertTrue(
            all(row["delta"] == "" for row in rows if row["field"] == "gauge")
        )
        self.assertFalse(result["warnings"])

    def test_session_generation_schema_and_reset(self):
        result = self.report(
            [
                sample(),
                sample(2, 11),
                sample(3, 20, generation="b"),
                sample(4, 25, generation="b"),
                sample(5, 30, generation="b", version=2),
                sample(6, 1, generation="b", version=2),
                sample(session="two", count=900),
            ]
        )
        self.assertEqual(
            result["sessions"]["one"]["counterDeltas"]["new.module"]["requests"], 6
        )
        self.assertEqual(result["sessions"]["two"]["counterDeltas"], {})
        self.assertTrue(any("reset" in warning for warning in result["warnings"]))

    def test_index_identity_and_absence_reset(self):
        changed = sample(2, 900)
        changed["records"][0]["values"]["identity"] = "different"
        missing = sample(3)
        missing["records"] = []
        result = self.report([sample(), changed, missing, sample(4, 1000)])
        self.assertEqual(result["sessions"]["one"]["counterDeltas"], {})

    def test_duplicate_gap_and_bad_order(self):
        result = self.report(
            [sample(), sample(), sample(4, 20), sample(5, 30, elapsed=0)]
        )
        self.assertEqual(result["sessions"]["one"]["samples"], 2)
        self.assertEqual(len(result["warnings"]), 3)

    def test_partial_live_line_and_utf8(self):
        source, target = self.root / "live", self.root / "copy"
        tail = Tail()
        payload = json.dumps(sample(), ensure_ascii=False).encode() + b"\n"
        source.write_bytes(payload[:-3])
        tail.copy(source, target)
        self.assertEqual(target.read_bytes(), b"")
        with source.open("ab") as file:
            file.write(payload[-3:] + "Grüße".encode()[:4])
        tail.copy(source, target)
        self.assertEqual(target.read_bytes(), payload)
        with source.open("ab") as file:
            file.write("Grüße".encode()[4:] + b"\n")
        tail.copy(source, target)
        self.assertEqual(target.read_bytes(), payload + "Grüße\n".encode())
        tail.copy(source, target)
        self.assertEqual(target.read_bytes(), payload + "Grüße\n".encode())

    def test_log_rotation(self):
        source, target = self.root / "log", self.root / "copy"
        tail = Tail()
        source.write_bytes(b"old log\n")
        tail.copy(source, target)
        source.write_bytes(b"new log longer\n")
        tail.copy(source, target)
        self.assertEqual(target.read_bytes(), b"old log\nnew log longer\n")

    def test_bad_and_truncated_rows_reported(self):
        source = self.root / "bad.jsonl"
        source.write_bytes(b"bad\n" + json.dumps(sample()).encode() + b"\n{partial")
        result = analyze(source, self.root / "report")
        self.assertEqual(len(result["warnings"]), 2)
        self.assertEqual(result["sessions"]["one"]["samples"], 1)

    def test_reject_invalid_numeric_counter(self):
        for value in (True, "10", float("nan"), float("inf"), 10**500):
            with self.subTest(value=str(value)[:15]), self.assertRaises(ValueError):
                Frame.parse(sample(count=value))

    def test_collect_without_game_and_no_overwrite(self):
        source = self.root / "sessions"
        source.mkdir()
        (source / "one.jsonl").write_text(json.dumps(sample()) + "\n", encoding="utf-8")
        output = self.root / "capture"
        collect(source, output, 0, [])
        self.assertTrue((output / "raw/one.jsonl").is_file())
        with self.assertRaises(FileExistsError):
            collect(source, output, 0, [])


    def test_new_session_appears_while_collecting(self):
        source = self.root / "sessions"
        source.mkdir()
        (source / "one.jsonl").write_text(json.dumps(sample()) + "\n", encoding="utf-8")
        def restart():
            (source / "two.jsonl").write_text(json.dumps(sample(session="two")) + "\n", encoding="utf-8")
        timer = threading.Timer(0.05, restart)
        timer.start()
        try:
            output = self.root / "capture"
            collect(source, output, 0.15, [])
            summary = json.loads((output / "summary.json").read_text(encoding="utf-8"))
            self.assertEqual(set(summary["sessions"]), {"one", "two"})
        finally:
            timer.join()

    def test_empty_capture_is_not_a_successful_measurement(self):
        self.assertTrue(self.report([])["warnings"])


if __name__ == "__main__":
    unittest.main()
