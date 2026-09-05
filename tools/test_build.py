"""Distribution and tooling boundaries, without launching or modifying a game."""
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import build


class BuildTests(unittest.TestCase):
    def test_current_project_and_staging(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            (output / "FixWorld.dll").touch()
            with patch.object(build, "OUTPUT", output), patch.object(build, "run_build") as run:
                build.build_mod()
                project, properties = run.call_args.args
                self.assertEqual(project, build.MOD / "FixWorld.csproj")
                self.assertIn(f"-p:OutputPath={output.as_posix()}/", properties)

    def test_invalid_explicit_references_fail_before_build(self) -> None:
        with tempfile.TemporaryDirectory() as directory, patch.object(build, "run_build") as run:
            with self.assertRaises(RuntimeError):
                build.build_mod(Path(directory))
            with self.assertRaises(RuntimeError):
                build.build_mod(harmony=Path(directory) / "missing.dll")
            run.assert_not_called()

    def test_package_excludes_stale_binaries_and_requires_current_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            content, output = root / "content", root / "output"
            required = {
                "About/About.xml", "LoadFolders.xml", "Tools/Doorstop-4.4.0/winhttp.dll"
            }
            for name in required | {"v1.6/Assemblies/Old.dll", "Tools/secret.log", "Source/private.cs"}:
                path = content / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.touch()
            output.mkdir()
            with self.assertRaises(RuntimeError):
                build.package_files(content, output)
            for name in ("FixWorld.dll", "FixWorld.Bootstrap.dll", "FixWorld.Restart.exe", "FixWorld.Restart.exe.config", "Assembly-CSharp.dll", "0Harmony.dll"):
                (output / name).touch()
            files = build.package_files(content, output)
            self.assertEqual({p for p in files if p.endswith('.dll')}, {
                "v1.6/Assemblies/FixWorld.dll", "v1.6/Assemblies/FixWorld.Bootstrap.dll",
                "Tools/Doorstop-4.4.0/winhttp.dll"
            })
            self.assertNotIn("Tools/secret.log", files)
            self.assertNotIn("Source/private.cs", files)


if __name__ == "__main__":
    unittest.main()
