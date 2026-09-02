from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes
from dataclasses import dataclass, replace
import os
from pathlib import Path
import re
import subprocess
import time
import winreg


class Rect(ctypes.Structure):
    _fields_ = [
        ("left", wintypes.LONG),
        ("top", wintypes.LONG),
        ("right", wintypes.LONG),
        ("bottom", wintypes.LONG),
    ]


class MonitorInfo(ctypes.Structure):
    _fields_ = [
        ("size", wintypes.DWORD),
        ("monitor", Rect),
        ("work_area", Rect),
        ("flags", wintypes.DWORD),
        ("device_name", wintypes.WCHAR * 32),
    ]


class DisplayDevice(ctypes.Structure):
    _fields_ = [
        ("size", wintypes.DWORD),
        ("device_name", wintypes.WCHAR * 32),
        ("device_string", wintypes.WCHAR * 128),
        ("state_flags", wintypes.DWORD),
        ("device_id", wintypes.WCHAR * 128),
        ("device_key", wintypes.WCHAR * 128),
    ]


@dataclass(frozen=True)
class Monitor:
    handle: int
    device_name: str
    friendly_name: str
    left: int
    top: int
    width: int
    height: int
    primary: bool
    used_fallback: bool = False

    @property
    def unity_index(self) -> int:
        match = re.search(r"DISPLAY(\d+)$", self.device_name, re.IGNORECASE)
        if not match:
            raise RuntimeError(
                f"Cannot derive a Unity monitor from {self.device_name!r}."
            )
        return int(match.group(1))


@dataclass
class RimWorldProcess:
    process: subprocess.Popen[bytes]
    window: int
    actual_monitor: str

    def close(self) -> None:
        if self.process.poll() is not None:
            return
        USER32.PostMessageW(self.window, 0x0010, 0, 0)
        try:
            self.process.wait(timeout=10)
            return
        except subprocess.TimeoutExpired:
            self.process.terminate()
        try:
            self.process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait(timeout=5)


USER32 = ctypes.WinDLL("user32", use_last_error=True)
MONITOR_ENUM_PROC = ctypes.WINFUNCTYPE(
    wintypes.BOOL,
    wintypes.HMONITOR,
    wintypes.HDC,
    ctypes.POINTER(Rect),
    wintypes.LPARAM,
)
WINDOW_ENUM_PROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

USER32.EnumDisplayMonitors.argtypes = (
    wintypes.HDC,
    ctypes.POINTER(Rect),
    MONITOR_ENUM_PROC,
    wintypes.LPARAM,
)
USER32.EnumDisplayMonitors.restype = wintypes.BOOL
USER32.GetMonitorInfoW.argtypes = (wintypes.HMONITOR, ctypes.POINTER(MonitorInfo))
USER32.GetMonitorInfoW.restype = wintypes.BOOL
USER32.EnumDisplayDevicesW.argtypes = (
    wintypes.LPCWSTR,
    wintypes.DWORD,
    ctypes.POINTER(DisplayDevice),
    wintypes.DWORD,
)
USER32.EnumDisplayDevicesW.restype = wintypes.BOOL
USER32.EnumWindows.argtypes = (WINDOW_ENUM_PROC, wintypes.LPARAM)
USER32.EnumWindows.restype = wintypes.BOOL
USER32.GetWindowThreadProcessId.argtypes = (
    wintypes.HWND,
    ctypes.POINTER(wintypes.DWORD),
)
USER32.GetWindowThreadProcessId.restype = wintypes.DWORD
USER32.IsWindowVisible.argtypes = (wintypes.HWND,)
USER32.IsWindowVisible.restype = wintypes.BOOL
USER32.ShowWindow.argtypes = (wintypes.HWND, ctypes.c_int)
USER32.ShowWindow.restype = wintypes.BOOL
USER32.SetWindowPos.argtypes = (
    wintypes.HWND,
    wintypes.HWND,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.c_int,
    wintypes.UINT,
)
USER32.SetWindowPos.restype = wintypes.BOOL
USER32.MonitorFromWindow.argtypes = (wintypes.HWND, wintypes.DWORD)
USER32.MonitorFromWindow.restype = wintypes.HMONITOR
USER32.PostMessageW.argtypes = (
    wintypes.HWND,
    wintypes.UINT,
    wintypes.WPARAM,
    wintypes.LPARAM,
)
USER32.PostMessageW.restype = wintypes.BOOL


def resolve_game_root(value: Path | None) -> Path:
    if value is not None:
        return value.resolve()
    configured = os.environ.get("RIMWORLD_ROOT")
    if configured:
        return Path(configured).resolve()
    raise RuntimeError("Set RIMWORLD_ROOT or pass --game-root <RimWorldRoot>.")


def select_monitor(friendly_name: str | None, fallback_index: int) -> Monitor:
    monitors = _enumerate_monitors()
    if friendly_name:
        requested = friendly_name.casefold()
        for monitor in monitors:
            if monitor.friendly_name.casefold() == requested:
                return monitor

    fallback_device = rf"\\.\DISPLAY{fallback_index}".casefold()
    fallback = next(
        (item for item in monitors if item.device_name.casefold() == fallback_device),
        next((item for item in monitors if item.primary), monitors[0]),
    )
    if friendly_name:
        print(
            f"Warning: monitor {friendly_name!r} is not active; using "
            f"{fallback.device_name}."
        )
    return replace(fallback, used_fallback=bool(friendly_name))


def is_rimworld_running() -> bool:
    result = subprocess.run(
        ["tasklist", "/FI", "IMAGENAME eq RimWorldWin64.exe", "/NH"],
        check=True,
        capture_output=True,
        text=True,
        errors="replace",
    )
    return "RimWorldWin64.exe" in result.stdout


def launch(
    executable: Path,
    working_directory: Path,
    arguments: list[str],
    environment: dict[str, str],
    monitor: Monitor,
    minimized: bool,
) -> RimWorldProcess:
    command = [
        str(executable),
        *arguments,
        "-monitor",
        str(monitor.unity_index),
        "-screen-width",
        str(monitor.width),
        "-screen-height",
        str(monitor.height),
    ]
    process = subprocess.Popen(command, cwd=working_directory, env=environment)
    try:
        window = _find_window(process.pid)
        actual_monitor = _place_window(window, monitor, minimized)
        return RimWorldProcess(process, window, actual_monitor)
    except Exception:
        if process.poll() is None:
            process.terminate()
        raise


def _enumerate_monitors() -> list[Monitor]:
    monitors: list[Monitor] = []

    @MONITOR_ENUM_PROC
    def callback(handle: int, _dc: int, _rect: object, _data: int) -> bool:
        info = MonitorInfo()
        info.size = ctypes.sizeof(MonitorInfo)
        if not USER32.GetMonitorInfoW(handle, ctypes.byref(info)):
            return True

        device = DisplayDevice()
        device.size = ctypes.sizeof(DisplayDevice)
        friendly_name = info.device_name
        if USER32.EnumDisplayDevicesW(info.device_name, 0, ctypes.byref(device), 0):
            friendly_name = _monitor_name_from_edid(device.device_id) or (
                device.device_string or info.device_name
            )

        monitors.append(
            Monitor(
                handle=int(handle),
                device_name=info.device_name,
                friendly_name=friendly_name,
                left=info.monitor.left,
                top=info.monitor.top,
                width=info.monitor.right - info.monitor.left,
                height=info.monitor.bottom - info.monitor.top,
                primary=bool(info.flags & 1),
            )
        )
        return True

    if not USER32.EnumDisplayMonitors(0, None, callback, 0):
        raise ctypes.WinError(ctypes.get_last_error())
    if not monitors:
        raise RuntimeError("Windows did not report an active monitor.")
    return monitors


def _monitor_name_from_edid(device_id: str) -> str | None:
    match = re.match(r"MONITOR\\([^\\]+)\\", device_id, re.IGNORECASE)
    if not match:
        return None
    display_path = rf"SYSTEM\CurrentControlSet\Enum\DISPLAY\{match.group(1)}"
    try:
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, display_path) as display_key:
            for index in range(winreg.QueryInfoKey(display_key)[0]):
                instance = winreg.EnumKey(display_key, index)
                parameters_path = display_path + rf"\{instance}\Device Parameters"
                try:
                    with winreg.OpenKey(
                        winreg.HKEY_LOCAL_MACHINE, parameters_path
                    ) as parameters_key:
                        edid, _ = winreg.QueryValueEx(parameters_key, "EDID")
                except OSError:
                    continue
                for offset in range(54, min(len(edid), 126), 18):
                    descriptor = edid[offset : offset + 18]
                    if descriptor[:5] == b"\x00\x00\x00\xfc\x00":
                        return (
                            descriptor[5:18]
                            .decode("ascii", errors="ignore")
                            .strip("\x00\n\r ")
                            or None
                        )
    except OSError:
        return None
    return None


def _find_window(process_id: int) -> int:
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        handles: list[int] = []

        @WINDOW_ENUM_PROC
        def callback(handle: int, _data: int) -> bool:
            owner = wintypes.DWORD()
            USER32.GetWindowThreadProcessId(handle, ctypes.byref(owner))
            if owner.value == process_id and USER32.IsWindowVisible(handle):
                handles.append(int(handle))
                return False
            return True

        USER32.EnumWindows(callback, 0)
        if handles:
            return handles[0]
        time.sleep(0.1)
    raise RuntimeError("RimWorld window was not found within 30 seconds.")


def _place_window(window: int, monitor: Monitor, minimized: bool) -> str:
    if minimized:
        USER32.ShowWindow(window, 6)
        return monitor.device_name

    deadline = time.monotonic() + 5
    actual_device = "unknown"
    while time.monotonic() < deadline:
        USER32.ShowWindow(window, 9)
        if not USER32.SetWindowPos(
            window,
            0,
            monitor.left,
            monitor.top,
            monitor.width,
            monitor.height,
            0x0014,
        ):
            raise ctypes.WinError(ctypes.get_last_error())
        USER32.ShowWindow(window, 3)
        time.sleep(0.25)

        handle = USER32.MonitorFromWindow(window, 2)
        info = MonitorInfo()
        info.size = ctypes.sizeof(MonitorInfo)
        if not USER32.GetMonitorInfoW(handle, ctypes.byref(info)):
            raise ctypes.WinError(ctypes.get_last_error())
        actual_device = info.device_name
        if actual_device.casefold() == monitor.device_name.casefold():
            return actual_device

    raise RuntimeError(
        f"RimWorld remained on {actual_device!r} instead of {monitor.device_name!r}."
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Start RimWorld on the configured monitor."
    )
    parser.add_argument(
        "--game-root",
        type=Path,
        help="RimWorld directory. Defaults to RIMWORLD_ROOT.",
    )
    parser.add_argument("--monitor-name")
    parser.add_argument("--monitor", type=int, choices=range(1, 17), default=1)
    parser.add_argument("--minimized", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    game_root = resolve_game_root(args.game_root)
    executable = game_root / "RimWorldWin64.exe"
    if not executable.is_file():
        raise RuntimeError(f"RimWorld does not exist: {executable}")
    if is_rimworld_running():
        raise RuntimeError("RimWorld is already running.")

    monitor = select_monitor(args.monitor_name, args.monitor)
    rimworld = launch(
        executable,
        game_root,
        ["-popupwindow"],
        os.environ.copy(),
        monitor,
        args.minimized,
    )
    style = "minimized" if args.minimized else "maximized"
    print(
        f"RimWorld is running on {monitor.friendly_name} "
        f"({rimworld.actual_monitor}), {style}."
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f"error: {error}")
        raise SystemExit(1)
