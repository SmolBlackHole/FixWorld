// SPDX-License-Identifier: MPL-2.0
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace FixWorld.Bootstrap
{
    public enum InstallationStatus { Missing, Current, RepairRequired, Conflict }
    public readonly struct InstallationState
    {
        public InstallationState(InstallationStatus status, bool restartPending, string message)
        { Status = status; RestartPending = restartPending; Message = message; }
        public InstallationStatus Status { get; }
        public bool RestartPending { get; }
        public string Message { get; }
    }

    public sealed class Installation
    {
        public const string DoorstopHash = "93406D0A02E7C164B89828CBFE3B289930A112D2ECA50BD4A52E72ECE169E6A8";
        private const string Marker = "# Managed by FixWorld Bootstrap";
        private readonly string gameRoot, proxySource, bootstrap, expectedProxyHash;
        private string Proxy => Path.Combine(gameRoot, "winhttp.dll");
        private string Config => Path.Combine(gameRoot, "doorstop_config.ini");
        private string ManifestPath => Path.Combine(gameRoot, "FixWorld.bootstrap.json");
        public string Helper { get; }

        public Installation(string gameRoot, string modRoot) : this(gameRoot,
            Path.Combine(modRoot, "Tools", "Doorstop-4.4.0", "winhttp.dll"),
            Path.Combine(modRoot, "v1.6", "Assemblies", "FixWorld.Bootstrap.dll"),
            Path.Combine(modRoot, "Tools", "FixWorld.Restart.exe"), DoorstopHash)
        { }

        // Explicit paths also allow tests to operate exclusively on fixtures.
        public Installation(string gameRoot, string proxySource, string bootstrap, string helper, string expectedProxyHash)
        {
            this.gameRoot = Path.GetFullPath(gameRoot);
            this.proxySource = Path.GetFullPath(proxySource);
            this.bootstrap = Path.GetFullPath(bootstrap);
            Helper = Path.GetFullPath(helper);
            this.expectedProxyHash = expectedProxyHash;
        }

        public InstallationState Inspect()
        {
            var manifest = ReadManifest();
            bool pending = manifest?.RestartPending == true;
            if (File.Exists(ManifestPath) && manifest == null)
                return State(InstallationStatus.Conflict, false, "Unknown or corrupt bootstrap manifest.");
            if (manifest == null)
            {
                if (File.Exists(Proxy) || File.Exists(Config))
                    return State(InstallationStatus.Conflict, false, "Existing proxy/config is not owned by this bootstrap. Remove the old installation explicitly first.");
                return State(InstallationStatus.Missing, false, "Doorstop is not installed.");
            }
            string actualProxy = File.Exists(Proxy) ? Hash(Proxy) : null;
            string actualConfig = File.Exists(Config) ? Hash(Config) : null;
            if ((actualProxy != null && actualProxy != manifest.ProxyHash && (!pending || actualProxy != manifest.PreviousProxyHash)) ||
                (actualConfig != null && actualConfig != manifest.ConfigHash && (!pending || actualConfig != manifest.PreviousConfigHash)))
                return State(InstallationStatus.Conflict, pending, "Installed files differ from the ownership manifest; no files will be overwritten.");
            bool current = actualProxy == manifest.ProxyHash && actualConfig == manifest.ConfigHash && File.Exists(bootstrap) &&
                manifest.ProxyHash == expectedProxyHash && manifest.BootstrapHash == Hash(bootstrap) &&
                string.Equals(manifest.BootstrapPath, bootstrap, StringComparison.OrdinalIgnoreCase) &&
                manifest.ConfigHash == HashBytes(Encoding.UTF8.GetBytes(Configuration()));
            return State(current ? InstallationStatus.Current : InstallationStatus.RepairRequired, pending,
                current ? "Doorstop installation is current." : "Owned bootstrap installation needs repair.");
        }

        public void Install()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("Windows bootstrap only.");
            if (!File.Exists(Path.Combine(gameRoot, "RimWorldWin64.exe")))
                throw new FileNotFoundException("RimWorldWin64.exe is missing.");
            var state = Inspect();
            if (state.Status == InstallationStatus.Conflict || state.RestartPending)
                throw new InvalidOperationException(state.Message + " Automatic installation/restart is not retried.");
            if (!File.Exists(proxySource) || Hash(proxySource) != expectedProxyHash)
                throw new InvalidDataException("Bundled Doorstop failed verification.");
            if (!File.Exists(bootstrap) || !File.Exists(Helper))
                throw new FileNotFoundException("Bootstrap DLL or restart helper is missing from the package.");
            string configuration = Configuration();
            var manifest = new Manifest
            {
                Schema = 1,
                ProxyHash = expectedProxyHash,
                BootstrapPath = bootstrap,
                BootstrapHash = Hash(bootstrap),
                ConfigHash = HashBytes(Encoding.UTF8.GetBytes(configuration)),
                RestartPending = true,
                PreviousProxyHash = File.Exists(Proxy) ? Hash(Proxy) : null,
                PreviousConfigHash = File.Exists(Config) ? Hash(Config) : null
            };
            // The ownership record is written first. Interrupted fresh installs retain
            // proof of intended files; pending state prevents automatic restart loops.
            WriteManifest(manifest);
            AtomicWrite(Proxy, File.ReadAllBytes(proxySource));
            AtomicWrite(Config, Encoding.UTF8.GetBytes(configuration));
        }

        public void ConfirmAttached()
        {
            var state = Inspect();
            if (state.Status != InstallationStatus.Current)
                throw new InvalidOperationException(state.Message);
            var manifest = ReadManifest();
            if (!manifest.RestartPending)
                return;
            manifest.RestartPending = false;
            manifest.PreviousProxyHash = manifest.PreviousConfigHash = null;
            WriteManifest(manifest);
        }

        public void Uninstall()
        {
            if (Inspect().Status == InstallationStatus.Conflict)
                throw new InvalidOperationException("Refusing to remove unowned files.");
            // No broad directory deletion and no deletion of the mod assembly.
            File.Delete(Config);
            File.Delete(Proxy);
            File.Delete(ManifestPath);
        }

        private string Configuration() => Marker + "\n# UnityDoorstop 4.4.0, Windows x64\n[General]\nenabled=true\ntarget_assembly=" + bootstrap +
            "\nredirect_output_log=false\nignore_disable_switch=false\n[UnityMono]\ndebug_enabled=false\n";
        private static InstallationState State(InstallationStatus status, bool pending, string message) => new(status, pending, message);
        private Manifest ReadManifest()
        {
            if (!File.Exists(ManifestPath))
                return null;
            try
            {
                using var stream = File.OpenRead(ManifestPath);
                var value = (Manifest)new DataContractJsonSerializer(typeof(Manifest)).ReadObject(stream);
                return value?.Schema == 1 && !string.IsNullOrEmpty(value.ProxyHash) && !string.IsNullOrEmpty(value.ConfigHash) &&
                    !string.IsNullOrEmpty(value.BootstrapHash) && !string.IsNullOrEmpty(value.BootstrapPath) ? value : null;
            }
            catch (SerializationException) { return null; }
        }
        private void WriteManifest(Manifest manifest)
        {
            using var stream = new MemoryStream();
            new DataContractJsonSerializer(typeof(Manifest)).WriteObject(stream, manifest);
            AtomicWrite(ManifestPath, stream.ToArray());
        }
        public static string Hash(string path) { using var stream = File.OpenRead(path); using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
        private static string HashBytes(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        private static void AtomicWrite(string path, byte[] bytes)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        [DataContract]
        private sealed class Manifest
        {
            [DataMember] public int Schema;
            [DataMember] public string ProxyHash;
            [DataMember] public string ConfigHash;
            [DataMember] public string BootstrapPath;
            [DataMember] public string BootstrapHash;
            [DataMember] public bool RestartPending;
            [DataMember] public string PreviousProxyHash;
            [DataMember] public string PreviousConfigHash;
        }
    }
}
