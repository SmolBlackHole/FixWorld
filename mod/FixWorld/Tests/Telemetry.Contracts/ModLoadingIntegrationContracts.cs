using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FixWorld.Patches;
using FixWorld.Telemetry;
using FixWorld.UI;
using Verse;

internal static class ModLoadingIntegrationContracts
{
    internal static void Run()
    {
        using var diagnostics = new LibraryDiagnostics();
        using var telemetry = new ModLoadingTelemetry(diagnostics);
        using var loading = new LoadingProgress(diagnostics.Store);
        LoadingHooks.Progress = loading;
        loading.Begin();
        ModLoadingHooks.Install(telemetry);
        try
        {
            var mod = new ModContentPack { PackageId = "fixture", Name = "Fixture" };
            mod.ReloadContent(false);
            mod.RunContent();
            DirectXmlLoader.XmlAssetsInModFolder(mod, "Defs");
            LoadedModManager.Owner = mod;
            LoadedModManager.CreateModClasses();
            telemetry.Publish();
            if (telemetry.Snapshot.Failure != null)
                throw new Exception(telemetry.Snapshot.Failure);
            var row = telemetry.Snapshot.Mods.Single(m => m.Id == "fixture");
            if (row.Times.Any(t => t <= 0))
                throw new Exception("Missing real Harmony timing boundary");
            if (row.Warnings != 2 || row.Errors != 1)
                throw new Exception("Missing constructor/content/worker attribution");
            if (!LoadedModManager.Caught)
                throw new Exception("Original caller must still receive constructor exception");
        }
        finally { ModLoadingHooks.Uninstall(); LoadingHooks.Progress = null; Log.Errors.Clear(); }
        Console.WriteLine("PASS: production mod-loading Harmony adapters on fixtures: deferred content, parallel XML and constructor exception preservation.");
    }
}
namespace Verse
{
    internal sealed partial class ModContentPack
    {
        internal string PackageId, Name;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ReloadContent(bool hotReload) { System.Threading.Thread.SpinWait(500); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ReloadContentInt(bool hotReload) { Log.Warning("content warning"); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RunContent() { ReloadContentInt(false); }
    }
    internal static partial class Log
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Warning(string text) { }
    }
    internal sealed class LoadableXmlAsset
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public LoadableXmlAsset(FileInfo file, ModContentPack mod) { Log.Warning("XML warning"); }
    }
    internal static class DirectXmlLoader
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static LoadableXmlAsset[] XmlAssetsInModFolder(ModContentPack mod, string folderPath)
        { return new[] { Task.Run(() => new LoadableXmlAsset(new FileInfo("fixture.xml"), mod)).GetAwaiter().GetResult() }; }
    }
    internal sealed class FixtureMod
    {
        public FixtureMod(ModContentPack mod) { throw new InvalidOperationException("fixture constructor"); }
    }
    internal static class LoadedModManager
    {
        internal static ModContentPack Owner;
        internal static bool Caught;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void CreateModClasses()
        {
            try
            { Activator.CreateInstance(typeof(FixtureMod), new object[] { Owner }); }
            catch (TargetInvocationException) { Caught = true; }
        }
    }
}
