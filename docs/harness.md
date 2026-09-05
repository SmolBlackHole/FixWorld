# Local telemetry capture

Parent: [Typed telemetry](telemetry.md)

The current fork exports published module DTOs through the same contracts used
by logs. `tools/telemetry.py` collects and analyzes those records locally. It does
not use the HugsLib Shell wrapper, upload data, launch RimWorld or close it.
The old `tools/benchmark.py` is a legacy loader/schema-19 runner, not the entry
point for this rewrite. Do not run it against the new fork.

## Capture a manually started game

After building/deploying the new fork and restarting the game once:

```powershell
python tools/telemetry.py collect --seconds 60 --game-root D:/SteamLibrary/steamapps/common/RimWorld
```

Without `--seconds`, collection continues until Ctrl+C. The game stays open.
Default input is `<RimWorld savedata>/FixWorld/Telemetry`. For a custom
`-savedatafolder`, pass `--source <that-folder>/FixWorld/Telemetry` explicitly.
`--log <path>` can be repeated to collect additional or redirected logs.
By default the collector reads Player.log beside the savedata folder's Config
directory. `--game-root` adds the early `FixWorld.Bootstrap.log`.

The collector follows the newest existing capture and any new sessions appearing
after it starts. It includes the current session's existing history, not just the
last 60 seconds. If the game is closed, the newest file may be an older run:
check UTC timestamps and sample counts. A session is not proof of a live process.
No telemetry file is expected before early core initialization or with FixWorld
disabled. Bootstrap/Player logs remain the diagnostic path for those cases.

Output goes into a new, unique `data/profiling/captures/runtime/<UTC timestamp>`
directory. An explicit `--output` must not already exist.

```text
capture/
  raw/<session>.jsonl    complete original records, retained for re-analysis
  log-0-Player.log       copied complete log lines (when present)
  log-1-...             other explicitly selected logs
  metrics.csv           every module field, kind and eligible counter delta
  summary.json          sessions, accumulated deltas, validation warnings
```

Analyze saved data without a running game:

```powershell
python tools/telemetry.py analyze data/profiling/captures/runtime/<capture>/raw --output temp/analysis
```

Analysis overwrites `metrics.csv` and `summary.json` in the given output directory,
never the raw JSONL. Validation warnings make the analyze command exit nonzero.
Missing/invalid measurements are not benchmark passes. Logs may contain private
paths, mod lists and chat. These are local raw captures, not redacted uploads.

## Ownership and transport

- Controller starts exactly one `TelemetryCapture` with its early core and stops
  it before disposing the store. It uses a background thread with a one-second
  wait between writes, not an event for every probe or a queue of copied DTOs.
- DTO references are read from the store. Presenters execute off-thread and must
  be pure: no Unity/Verse reads, mutable owner state or thread-affine formatting.
- Each complete JSON object is formatted before it touches the file, then written
  with a newline and flushed. Encoding is UTF-8 without BOM. JSON serialization
  necessarily allocates a text buffer; this is not end-to-end zero-copy I/O.
- Each process creates a unique GUID-named file. The envelope has schema version,
  session, PID, sequence, UTC, monotonic elapsed seconds and the store's records.
  Module records retain ID, schema, registration generation, values and declared
  counters. No module-specific field map exists in exporter or Python.
- Files stop growing at 64 MiB per session. A cap, disk error or broken presenter
  stops that exporter and reports to the bootstrap log, without failing startup.
  Old sessions are not deleted automatically. Remove unwanted local captures
  manually; storage across many launches can accumulate.
- Shutdown signals the worker and waits at most two seconds. A stuck write cannot
  hold shutdown forever. The background worker owns file closure even if the
  caller times out. A crash can lose buffered data or leave a partial last line;
  Python ignores incomplete lines until finished. There is no crash verdict based
  solely on an absent final record.
- The collector copies complete log lines incrementally and notices truncation
  or changed prefixes. It cannot recover logs overwritten between polls, or
  distinguish a replacement with exactly the same observed prefix and length.

## Reading measurements correctly

`writer.Value(...)` describes state. `writer.Counter(...)` declares cumulative
work, using the same typed presenter for JSON and log output. Python computes
deltas only for declared counters. It resets baselines across sessions, module
schema/registration changes, missing modules, counter-set changes and textual
identity changes (including reordering indexed profile/cache rows). A decreasing
counter is a reset, never negative work. Producers must re-register on a new
counter lifetime, rather than silently resetting to an indistinguishable value.

Snapshots are not globally atomic: providers publish independently. Repeated
snapshots while paused are valid, not new simulation ticks. Counter intervals
use export time; they are not exact probe durations or provider update intervals.
`max_ms` is a cumulative maximum, not an interval maximum. Nested inclusive times
must not be added up as total tick cost. Library tick notifications are still not
a TPS counter, and these probes do not measure the entire RimWorld simulation.

## Verification

```powershell
dotnet run --project mod/FixWorld/Tests/Telemetry.Contracts/FixWorld.Telemetry.Contracts.csproj -c Release
python -m unittest discover -s tools -p test_telemetry.py
```

The C# suite includes real worker/file tests and can write a production-format
fixture to `FIXWORLD_CAPTURE_TEST_OUTPUT` for analysis by the Python CLI. These
tests do not replace native Unity Mono/in-game acceptance. No game is started by
the tests or collector.
