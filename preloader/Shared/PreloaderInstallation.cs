using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FixWorld.Preloader
{
    internal enum PreloaderStatus
    {
        Unsupported,
        Unavailable,
        NotInstalled,
        Enabled,
        Disabled,
        Conflict,
        Incomplete
    }

    internal readonly struct PreloaderState
    {
        internal PreloaderStatus Status { get; }
        internal string Message { get; }
        internal bool ActiveThisLaunch { get; }

        internal PreloaderState(
            PreloaderStatus status,
            string message,
            bool activeThisLaunch)
        {
            Status = status;
            Message = message;
            ActiveThisLaunch = activeThisLaunch;
        }
    }

    internal sealed class PreloaderInstallationPaths
    {
        internal string GameRoot { get; }
        internal string BundledDoorstop { get; }
        internal string BundledPreloader { get; }

        internal PreloaderInstallationPaths(
            string gameRoot,
            string bundledDoorstop,
            string bundledPreloader)
        {
            GameRoot = Path.GetFullPath(gameRoot);
            BundledDoorstop = Path.GetFullPath(bundledDoorstop);
            BundledPreloader = Path.GetFullPath(bundledPreloader);
        }

        internal string Target(string fileName)
        {
            return Path.Combine(GameRoot, fileName);
        }
    }

    internal static class PreloaderInstallation
    {
        internal const string DoorstopSha256 =
            "93406D0A02E7C164B89828CBFE3B289930A112D2ECA50BD4A52E72ECE169E6A8";

        private const string OwnershipMarker = "# Managed by FixWorld";
        private const string DoorstopFileName = "winhttp.dll";
        private const string DoorstopConfigFileName = "doorstop_config.ini";

        private static readonly string[] InstalledFiles =
        {
            DoorstopConfigFileName,
            DoorstopFileName
        };

        internal static PreloaderState GetState(PreloaderInstallationPaths paths)
        {
            bool active = string.Equals(
                Environment.GetEnvironmentVariable("FIXWORLD_PRELOADER_ACTIVE"),
                "1",
                StringComparison.Ordinal);

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return State(
                    PreloaderStatus.Unsupported,
                    "The early loader is currently available only on Windows.",
                    active);
            }

            if (!File.Exists(Path.Combine(paths.GameRoot, "RimWorldWin64.exe")))
            {
                return State(
                    PreloaderStatus.Unavailable,
                    "FixWorld could not locate the RimWorld game directory.",
                    active);
            }

            if (!ValidateBundledFiles(paths, out string validationError))
            {
                return State(PreloaderStatus.Unavailable, validationError, active);
            }

            string doorstopPath = paths.Target(DoorstopFileName);
            string doorstopConfigPath = paths.Target(DoorstopConfigFileName);
            bool anyFile = File.Exists(doorstopPath) ||
                           File.Exists(doorstopConfigPath);
            if (!anyFile)
            {
                return State(
                    PreloaderStatus.NotInstalled,
                    "The required early loader is not installed.",
                    active);
            }

            if (File.Exists(doorstopPath) &&
                !string.Equals(Hash(doorstopPath), DoorstopSha256, StringComparison.Ordinal))
            {
                return Conflict(
                    "RimWorld already has an unknown winhttp.dll. FixWorld will not overwrite it.",
                    active);
            }

            if (File.Exists(doorstopConfigPath) &&
                !IsOwnedDoorstopConfig(doorstopConfigPath))
            {
                return Conflict(
                    "RimWorld already has a Doorstop config not owned by FixWorld.",
                    active);
            }

            if (!File.Exists(doorstopPath) || !File.Exists(doorstopConfigPath))
            {
                return State(
                    PreloaderStatus.Incomplete,
                    "The FixWorld early-loader installation is incomplete.",
                    active);
            }

            bool enabled = ReadEnabled(doorstopConfigPath);
            string activity = active ? " It is active in this launch." : string.Empty;
            return State(
                enabled ? PreloaderStatus.Enabled : PreloaderStatus.Disabled,
                "The FixWorld early loader is installed and " +
                (enabled ? "enabled." : "disabled.") + activity,
                active);
        }

        internal static PreloaderState Install(PreloaderInstallationPaths paths)
        {
            PreloaderState state = GetState(paths);
            if (state.Status != PreloaderStatus.NotInstalled &&
                state.Status != PreloaderStatus.Enabled &&
                state.Status != PreloaderStatus.Disabled &&
                state.Status != PreloaderStatus.Incomplete)
            {
                throw new InvalidOperationException(state.Message);
            }

            List<string> createdFiles = new List<string>();
            try
            {
                CopyDoorstop(paths, createdFiles);
                WriteDoorstopConfig(paths, createdFiles);
                return GetState(paths);
            }
            catch
            {
                for (int index = createdFiles.Count - 1; index >= 0; index--)
                {
                    TryDelete(createdFiles[index]);
                }

                throw;
            }
        }

        internal static void Uninstall(PreloaderInstallationPaths paths)
        {
            PreloaderState state = GetState(paths);
            if (state.Status != PreloaderStatus.Enabled &&
                state.Status != PreloaderStatus.Disabled)
            {
                throw new InvalidOperationException(state.Message);
            }

            foreach (string fileName in InstalledFiles)
            {
                File.Delete(paths.Target(fileName));
            }
        }

        private static bool ValidateBundledFiles(
            PreloaderInstallationPaths paths,
            out string error)
        {
            if (!File.Exists(paths.BundledDoorstop))
            {
                error = "The bundled UnityDoorstop file is missing.";
                return false;
            }

            if (!string.Equals(
                    Hash(paths.BundledDoorstop),
                    DoorstopSha256,
                    StringComparison.Ordinal))
            {
                error = "The bundled UnityDoorstop file failed its SHA-256 check.";
                return false;
            }

            if (!File.Exists(paths.BundledPreloader))
            {
                error = "The bundled FixWorld preloader is missing.";
                return false;
            }

            error = null;
            return true;
        }

        private static void CopyDoorstop(
            PreloaderInstallationPaths paths,
            ICollection<string> createdFiles)
        {
            string destination = paths.Target(DoorstopFileName);
            if (File.Exists(destination))
            {
                return;
            }

            CopyAtomic(paths.BundledDoorstop, destination);
            createdFiles.Add(destination);
        }

        private static void WriteDoorstopConfig(
            PreloaderInstallationPaths paths,
            ICollection<string> createdFiles = null)
        {
            string path = paths.Target(DoorstopConfigFileName);
            bool existed = File.Exists(path);
            string content = OwnershipMarker + Environment.NewLine +
                             "# UnityDoorstop 4.4.0, Windows x64" + Environment.NewLine +
                             "[General]" + Environment.NewLine +
                             "enabled=true" + Environment.NewLine +
                             "target_assembly=" + paths.BundledPreloader + Environment.NewLine +
                             "redirect_output_log=false" + Environment.NewLine +
                             "boot_config_override=" + Environment.NewLine +
                             "ignore_disable_switch=false" + Environment.NewLine +
                             Environment.NewLine +
                             "[UnityMono]" + Environment.NewLine +
                             "dll_search_path_override=" + Environment.NewLine +
                             "debug_enabled=false" + Environment.NewLine +
                             "debug_suspend=false" + Environment.NewLine +
                             "debug_address=127.0.0.1:10000" + Environment.NewLine;
            WriteAtomic(path, content, existed);
            if (!existed && createdFiles != null)
            {
                createdFiles.Add(path);
            }
        }

        private static bool ReadEnabled(string path)
        {
            foreach (string line in File.ReadAllLines(path))
            {
                if (!line.Trim().StartsWith("enabled=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return string.Equals(
                    line.Substring(line.IndexOf('=') + 1).Trim(),
                    "true",
                    StringComparison.OrdinalIgnoreCase);
            }

            throw new InvalidDataException("Doorstop config has no enabled setting.");
        }

        private static bool IsOwnedDoorstopConfig(string path)
        {
            return File.ReadAllText(path).StartsWith(
                OwnershipMarker,
                StringComparison.Ordinal);
        }

        private static string Hash(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "");
            }
        }

        private static void CopyAtomic(string source, string destination)
        {
            string temporaryPath = TemporaryPath(destination);
            File.Copy(source, temporaryPath, false);
            try
            {
                File.Move(temporaryPath, destination);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private static void WriteAtomic(string path, string content, bool replace)
        {
            string temporaryPath = TemporaryPath(path);
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            try
            {
                if (replace)
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private static string TemporaryPath(string destination)
        {
            return destination + ".fixworld-" + Guid.NewGuid().ToString("N") + ".tmp";
        }

        private static PreloaderState State(
            PreloaderStatus status,
            string message,
            bool active)
        {
            return new PreloaderState(status, message, active);
        }

        private static PreloaderState Conflict(string message, bool active)
        {
            return State(PreloaderStatus.Conflict, message, active);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
