// SPDX-License-Identifier: MPL-2.0
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Xml;

namespace Doorstop
{
    public static class Entrypoint
    {
        public static void Start() => FixWorld.Bootstrap.EarlyEntry.Start();
    }
}

namespace FixWorld.Bootstrap
{
    public static class BootEnvironment
    {
        public const string PackageId = "smolblackhole.fixworld";
        public static string GameRoot => Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
        public static string SaveDataFolder(string[] arguments)
        {
            for (int i = 0; i < arguments.Length; ++i)
            {
                if (arguments[i].StartsWith("-savedatafolder=", StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(arguments[i].Substring("-savedatafolder=".Length));
                if (string.Equals(arguments[i], "-savedatafolder", StringComparison.OrdinalIgnoreCase))
                {
                    if (++i == arguments.Length)
                        throw new ArgumentException("Missing savedatafolder argument.");
                    return Path.GetFullPath(arguments[i]);
                }
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios");
        }
        public static bool IsActive(string saveDataFolder)
        {
            var path = Path.Combine(saveDataFolder, "Config", "ModsConfig.xml");
            if (!File.Exists(path))
                return false;
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(path, settings))
                document.Load(reader);
            foreach (XmlNode node in document.SelectNodes("/ModsConfigData/activeMods/li"))
                if (string.Equals(node.InnerText.Trim(), PackageId, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
        public static void Log(string message)
        {
            try
            { File.AppendAllText(Path.Combine(GameRoot, "FixWorld.Bootstrap.log"), DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine); }
            catch { Console.WriteLine("[FixWorld.Bootstrap] " + message); }
        }
    }

    internal static class EarlyEntry
    {
        private static readonly object sync = new();
        private static bool starting;
        internal static void Start()
        {
            try
            {
                if (!BootEnvironment.IsActive(BootEnvironment.SaveDataFolder(Environment.GetCommandLineArgs())))
                {
                    BootSession.Current.Enter(false);
                    return;
                }
                if (!BootSession.Current.Enter(true))
                    return;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                TryStart();
            }
            catch (Exception error) { Failed(error); }
        }
        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args) => TryStart();
        private static void TryStart()
        {
            lock (sync)
            {
                if (starting || BootSession.Current.Phase != BootPhase.Waiting)
                    return;
                bool game = false, harmony = false;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var name = assembly.GetName();
                    game |= name.Name == "Assembly-CSharp";
                    harmony |= name.Name == "0Harmony" && name.Version.Major == 2;
                }
                if (!game || !harmony)
                    return;
                starting = true;
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                try
                {
                    var path = Path.Combine(Path.GetDirectoryName(typeof(EarlyEntry).Assembly.Location), "FixWorld.dll");
                    var core = Assembly.LoadFrom(path);
                    if (!string.Equals(Path.GetFullPath(core.Location), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("FixWorld resolved from a different installation: " + core.Location);
                    core.GetType("FixWorld.FixWorldController", true).GetMethod("StartEarly", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                    if (BootSession.Current.Phase != BootPhase.CoreReady)
                        throw new InvalidOperationException("Early core did not become ready.");
                    BootEnvironment.Log("Early core ready; waiting for the normal ModContentPack attachment.");
                }
                catch (Exception error) { Failed(error); }
            }
        }
        private static void Failed(Exception error)
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            BootSession.Current.Fail(error);
            BootEnvironment.Log("Bootstrap failed for this launch: " + error);
        }
    }
}
