using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
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
        internal PreloaderState(
            PreloaderStatus status,
            string message,
            bool activeThisLaunch,
            bool restartPending)
        {
            Status = status;
            Message = message;
            ActiveThisLaunch = activeThisLaunch;
            RestartPending = restartPending;
        }

        internal PreloaderStatus Status { get; }

        internal string Message { get; }

        internal bool ActiveThisLaunch { get; }

        internal bool RestartPending { get; }
    }

    internal sealed class PreloaderInstallationPaths
    {
        internal PreloaderInstallationPaths(
            string gameRoot,
            string bundledDoorstop,
            string bundledPreloader)
            : this(
                gameRoot,
                bundledDoorstop,
                bundledPreloader,
                PreloaderInstallation.DoorstopSha256)
        {
        }

        internal PreloaderInstallationPaths(
            string gameRoot,
            string bundledDoorstop,
            string bundledPreloader,
            string doorstopSha256)
        {
            GameRoot = Path.GetFullPath(gameRoot);
            BundledDoorstop = Path.GetFullPath(bundledDoorstop);
            BundledPreloader = Path.GetFullPath(bundledPreloader);
            DoorstopSha256 = doorstopSha256 ??
                throw new ArgumentNullException(nameof(doorstopSha256));
        }

        internal string GameRoot { get; }

        internal string BundledDoorstop { get; }

        internal string BundledPreloader { get; }

        internal string DoorstopSha256 { get; }

        internal string Target(string fileName)
        {
            return Path.Combine(GameRoot, fileName);
        }
    }

    [DataContract]
    internal sealed class PreloaderInstallationManifest
    {
        internal const int CurrentSchemaVersion = 1;

        [DataMember(Name = "schemaVersion", Order = 1)]
        internal int SchemaVersion { get; set; }

        [DataMember(Name = "doorstopVersion", Order = 2)]
        internal string DoorstopVersion { get; set; }

        [DataMember(Name = "doorstopSha256", Order = 3)]
        internal string DoorstopSha256 { get; set; }

        [DataMember(Name = "configSha256", Order = 4)]
        internal string ConfigSha256 { get; set; }

        [DataMember(Name = "preloaderPath", Order = 5)]
        internal string PreloaderPath { get; set; }

        [DataMember(Name = "preloaderSha256", Order = 6)]
        internal string PreloaderSha256 { get; set; }

        [DataMember(Name = "restartPending", Order = 7)]
        internal bool RestartPending { get; set; }
    }

    internal static class PreloaderInstallation
    {
        internal const string DoorstopSha256 =
            "93406D0A02E7C164B89828CBFE3B289930A112D2ECA50BD4A52E72ECE169E6A8";

        private const string DoorstopVersion = "4.4.0";
        private const string OwnershipMarker = "# Managed by FixWorld";
        private const string DoorstopFileName = "winhttp.dll";
        private const string DoorstopConfigFileName = "doorstop_config.ini";
        private const string ManifestFileName = "FixWorld.preloader.json";

        private static readonly string[] InstalledFiles =
        {
            DoorstopConfigFileName,
            DoorstopFileName,
            ManifestFileName
        };

        internal static PreloaderState GetState(PreloaderInstallationPaths paths)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            bool active = IsActiveThisLaunch();
            PreloaderInstallationManifest manifest = ReadManifest(
                paths.Target(ManifestFileName));
            bool restartPending = manifest?.RestartPending == true;

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return State(
                    PreloaderStatus.Unsupported,
                    "The early loader is currently available only on Windows.",
                    active,
                    restartPending);
            }

            if (!File.Exists(Path.Combine(paths.GameRoot, "RimWorldWin64.exe")))
            {
                return State(
                    PreloaderStatus.Unavailable,
                    "FixWorld could not locate the RimWorld game directory.",
                    active,
                    restartPending);
            }

            if (!ValidateBundledFiles(paths, out string validationError))
            {
                return State(
                    PreloaderStatus.Unavailable,
                    validationError,
                    active,
                    restartPending);
            }

            string doorstopPath = paths.Target(DoorstopFileName);
            string configPath = paths.Target(DoorstopConfigFileName);
            string manifestPath = paths.Target(ManifestFileName);
            bool anyFile = File.Exists(doorstopPath) ||
                           File.Exists(configPath) ||
                           File.Exists(manifestPath);
            if (!anyFile)
            {
                return State(
                    PreloaderStatus.NotInstalled,
                    "The required early loader is not installed.",
                    active,
                    restartPending);
            }

            string installedDoorstopHash = File.Exists(doorstopPath)
                ? Hash(doorstopPath)
                : null;
            if (installedDoorstopHash != null &&
                !IsOwnedDoorstop(
                    installedDoorstopHash,
                    paths.DoorstopSha256,
                    manifest))
            {
                return Conflict(
                    "RimWorld already has an unknown winhttp.dll. " +
                    "FixWorld will not overwrite it.",
                    active,
                    restartPending);
            }

            if (File.Exists(configPath) && !IsOwnedConfig(configPath))
            {
                return Conflict(
                    "RimWorld already has a Doorstop config not owned by FixWorld.",
                    active,
                    restartPending);
            }

            if (!File.Exists(doorstopPath) || !File.Exists(configPath))
            {
                return State(
                    PreloaderStatus.Incomplete,
                    "The FixWorld early-loader installation is incomplete.",
                    active,
                    restartPending);
            }

            if (!string.Equals(
                    installedDoorstopHash,
                    paths.DoorstopSha256,
                    StringComparison.Ordinal))
            {
                return State(
                    PreloaderStatus.Incomplete,
                    "The FixWorld early loader requires a Doorstop update.",
                    active,
                    restartPending);
            }

            bool enabled;
            string configuredTarget;
            try
            {
                enabled = ReadBoolean(configPath, "enabled");
                configuredTarget = ReadValue(configPath, "target_assembly");
            }
            catch (InvalidDataException exception)
            {
                return State(
                    PreloaderStatus.Incomplete,
                    exception.Message,
                    active,
                    restartPending);
            }

            if (!TargetsMatch(configuredTarget, paths.BundledPreloader))
            {
                return State(
                    PreloaderStatus.Incomplete,
                    "The FixWorld early loader points to an outdated preloader path.",
                    active,
                    restartPending);
            }

            if (!ManifestMatches(paths, configPath, manifest))
            {
                return State(
                    PreloaderStatus.Incomplete,
                    "The FixWorld early-loader installation metadata is outdated.",
                    active,
                    restartPending);
            }

            string activity = active
                ? " It is active in this launch."
                : string.Empty;
            string pending = restartPending
                ? " A restart is pending."
                : string.Empty;
            return State(
                enabled ? PreloaderStatus.Enabled : PreloaderStatus.Disabled,
                "The FixWorld early loader is installed and " +
                (enabled ? "enabled." : "disabled.") + activity + pending,
                active,
                restartPending);
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

            InstallDoorstop(paths);
            WriteDoorstopConfig(paths);
            WriteManifest(paths, restartPending: true);
            return GetState(paths);
        }

        internal static PreloaderState ConfirmStarted(
            PreloaderInstallationPaths paths)
        {
            string doorstopPath = paths.Target(DoorstopFileName);
            string configPath = paths.Target(DoorstopConfigFileName);
            string configuredTarget = File.Exists(configPath)
                ? ReadValue(configPath, "target_assembly")
                : null;
            if (!File.Exists(doorstopPath) ||
                !string.Equals(
                    Hash(doorstopPath),
                    paths.DoorstopSha256,
                    StringComparison.Ordinal) ||
                !File.Exists(configPath) ||
                !IsOwnedConfig(configPath) ||
                !ReadBoolean(configPath, "enabled") ||
                !TargetsMatch(configuredTarget, paths.BundledPreloader))
            {
                throw new InvalidOperationException(
                    "The active FixWorld early-loader installation does not " +
                    "match the current mod package.");
            }

            if (!PathsEqual(configuredTarget, paths.BundledPreloader))
            {
                WriteDoorstopConfig(paths);
            }

            WriteManifest(paths, restartPending: false);
            return GetState(paths);
        }

        internal static void Uninstall(PreloaderInstallationPaths paths)
        {
            PreloaderState state = GetState(paths);
            if (state.Status != PreloaderStatus.Enabled &&
                state.Status != PreloaderStatus.Disabled &&
                state.Status != PreloaderStatus.Incomplete)
            {
                throw new InvalidOperationException(state.Message);
            }

            foreach (string fileName in InstalledFiles)
            {
                string path = paths.Target(fileName);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static bool IsActiveThisLaunch()
        {
            return string.Equals(
                Environment.GetEnvironmentVariable("FIXWORLD_PRELOADER_ACTIVE"),
                Process.GetCurrentProcess().Id.ToString(
                    CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
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
                    paths.DoorstopSha256,
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

        private static bool IsOwnedDoorstop(
            string installedHash,
            string currentHash,
            PreloaderInstallationManifest manifest)
        {
            return string.Equals(
                       installedHash,
                       currentHash,
                       StringComparison.Ordinal) ||
                   manifest != null &&
                   string.Equals(
                       installedHash,
                       manifest.DoorstopSha256,
                       StringComparison.Ordinal);
        }

        private static void InstallDoorstop(PreloaderInstallationPaths paths)
        {
            string destination = paths.Target(DoorstopFileName);
            if (File.Exists(destination) &&
                string.Equals(
                    Hash(destination),
                    paths.DoorstopSha256,
                    StringComparison.Ordinal))
            {
                return;
            }

            CopyAtomic(paths.BundledDoorstop, destination, File.Exists(destination));
        }

        private static void WriteDoorstopConfig(PreloaderInstallationPaths paths)
        {
            string path = paths.Target(DoorstopConfigFileName);
            string content = OwnershipMarker + Environment.NewLine +
                             "# UnityDoorstop " + DoorstopVersion +
                             ", Windows x64" + Environment.NewLine +
                             "[General]" + Environment.NewLine +
                             "enabled=true" + Environment.NewLine +
                             "target_assembly=" + paths.BundledPreloader +
                             Environment.NewLine +
                             "redirect_output_log=false" + Environment.NewLine +
                             "boot_config_override=" + Environment.NewLine +
                             "ignore_disable_switch=false" + Environment.NewLine +
                             Environment.NewLine +
                             "[UnityMono]" + Environment.NewLine +
                             "dll_search_path_override=" + Environment.NewLine +
                             "debug_enabled=false" + Environment.NewLine +
                             "debug_suspend=false" + Environment.NewLine +
                             "debug_address=127.0.0.1:10000" + Environment.NewLine;
            WriteAtomic(path, content);
        }

        private static void WriteManifest(
            PreloaderInstallationPaths paths,
            bool restartPending)
        {
            string configPath = paths.Target(DoorstopConfigFileName);
            PreloaderInstallationManifest manifest =
                new PreloaderInstallationManifest
                {
                    SchemaVersion =
                        PreloaderInstallationManifest.CurrentSchemaVersion,
                    DoorstopVersion = DoorstopVersion,
                    DoorstopSha256 = paths.DoorstopSha256,
                    ConfigSha256 = Hash(configPath),
                    PreloaderPath = paths.BundledPreloader,
                    PreloaderSha256 = Hash(paths.BundledPreloader),
                    RestartPending = restartPending
                };
            DataContractJsonSerializer serializer =
                new DataContractJsonSerializer(
                    typeof(PreloaderInstallationManifest));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, manifest);
                WriteAtomic(
                    paths.Target(ManifestFileName),
                    Encoding.UTF8.GetString(stream.ToArray()));
            }
        }

        private static PreloaderInstallationManifest ReadManifest(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(
                        typeof(PreloaderInstallationManifest));
                using (FileStream stream = File.OpenRead(path))
                {
                    return serializer.ReadObject(stream) as
                        PreloaderInstallationManifest;
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (SerializationException)
            {
                return null;
            }
        }

        private static bool ManifestMatches(
            PreloaderInstallationPaths paths,
            string configPath,
            PreloaderInstallationManifest manifest)
        {
            return manifest != null &&
                   manifest.SchemaVersion ==
                   PreloaderInstallationManifest.CurrentSchemaVersion &&
                   string.Equals(
                       manifest.DoorstopVersion,
                       DoorstopVersion,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       manifest.DoorstopSha256,
                       paths.DoorstopSha256,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       manifest.ConfigSha256,
                       Hash(configPath),
                       StringComparison.Ordinal) &&
                   TargetsMatch(
                       manifest.PreloaderPath,
                       paths.BundledPreloader) &&
                   string.Equals(
                       manifest.PreloaderSha256,
                       Hash(paths.BundledPreloader),
                       StringComparison.Ordinal);
        }

        private static bool ReadBoolean(string path, string key)
        {
            string value = ReadValue(path, key);
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            throw new InvalidDataException(
                "Doorstop config has an invalid " + key + " setting.");
        }

        private static string ReadValue(string path, string key)
        {
            string prefix = key + "=";
            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(prefix.Length).Trim();
                }
            }

            throw new InvalidDataException(
                "Doorstop config has no " + key + " setting.");
        }

        private static bool IsOwnedConfig(string path)
        {
            return File.ReadAllText(path).StartsWith(
                OwnershipMarker,
                StringComparison.Ordinal);
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) ||
                string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(left.Trim().Trim('"')),
                    Path.GetFullPath(right.Trim().Trim('"')),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception)
                when (exception is ArgumentException ||
                      exception is NotSupportedException ||
                      exception is PathTooLongException)
            {
                return false;
            }
        }

        private static bool TargetsMatch(string configured, string bundled)
        {
            if (PathsEqual(configured, bundled))
            {
                return true;
            }

            string configuredPath = configured?.Trim().Trim('"');
            return !string.IsNullOrWhiteSpace(configuredPath) &&
                   File.Exists(configuredPath) &&
                   File.Exists(bundled) &&
                   string.Equals(
                       Hash(configuredPath),
                       Hash(bundled),
                       StringComparison.Ordinal);
        }

        private static string Hash(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream))
                    .Replace("-", string.Empty);
            }
        }

        private static void CopyAtomic(
            string source,
            string destination,
            bool replace)
        {
            string temporaryPath = TemporaryPath(destination);
            File.Copy(source, temporaryPath, false);
            try
            {
                if (replace)
                {
                    File.Replace(temporaryPath, destination, null);
                }
                else
                {
                    File.Move(temporaryPath, destination);
                }
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private static void WriteAtomic(string path, string content)
        {
            string temporaryPath = TemporaryPath(path);
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path))
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
            return destination + ".fixworld-" +
                   Guid.NewGuid().ToString("N") + ".tmp";
        }

        private static PreloaderState State(
            PreloaderStatus status,
            string message,
            bool active,
            bool restartPending)
        {
            return new PreloaderState(
                status,
                message,
                active,
                restartPending);
        }

        private static PreloaderState Conflict(
            string message,
            bool active,
            bool restartPending)
        {
            return State(
                PreloaderStatus.Conflict,
                message,
                active,
                restartPending);
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
