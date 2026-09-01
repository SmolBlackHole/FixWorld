using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace FixWorld.Preloader
{
    internal static class InstalledHarmonyResolver
    {
        private const string HarmonyAssemblyName = "0Harmony";
        private const string HarmonyPackageId = "brrainz.harmony";
        private const string HarmonyWorkshopId = "2009463077";

        internal static Assembly Load(string gameRoot, PreloaderLog log)
        {
            Assembly loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(IsHarmonyAssembly);
            if (loaded != null)
            {
                Validate(loaded);
                return loaded;
            }

            string modRoot = FindModRoot(gameRoot) ??
                             throw new DirectoryNotFoundException(
                                 "The active Harmony mod could not be located.");
            string assemblyPath = FindAssembly(modRoot) ??
                                  throw new FileNotFoundException(
                                      "The active Harmony mod contains no 0Harmony.dll.",
                                      modRoot);
            Assembly harmony = Assembly.LoadFrom(assemblyPath);
            Validate(harmony);
            log.Write(
                "Loaded installed Harmony " + harmony.GetName().Version +
                " early from " + assemblyPath + ".");
            return harmony;
        }

        private static string FindModRoot(string gameRoot)
        {
            string localMods = Path.Combine(gameRoot, "Mods");
            if (Directory.Exists(localMods))
            {
                foreach (string directory in Directory.GetDirectories(localMods))
                {
                    if (HasPackageId(directory, HarmonyPackageId))
                    {
                        return directory;
                    }
                }
            }

            DirectoryInfo gameDirectory = new DirectoryInfo(gameRoot);
            DirectoryInfo steamApps = gameDirectory.Parent?.Parent;
            if (steamApps != null && string.Equals(
                    steamApps.Name,
                    "steamapps",
                    StringComparison.OrdinalIgnoreCase))
            {
                string workshop = Path.Combine(
                    steamApps.FullName,
                    "workshop",
                    "content",
                    "294100",
                    HarmonyWorkshopId);
                if (Directory.Exists(workshop) &&
                    HasPackageId(workshop, HarmonyPackageId))
                {
                    return workshop;
                }
            }

            return null;
        }

        private static bool HasPackageId(string modRoot, string expectedPackageId)
        {
            string aboutPath = Path.Combine(modRoot, "About", "About.xml");
            if (!File.Exists(aboutPath))
            {
                return false;
            }

            try
            {
                XmlDocument document = new XmlDocument { XmlResolver = null };
                document.Load(aboutPath);
                string packageId = document.SelectSingleNode(
                    "/ModMetaData/packageId")?.InnerText?.Trim();
                return string.Equals(
                    packageId,
                    expectedPackageId,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (XmlException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string FindAssembly(string modRoot)
        {
            string[] preferred =
            {
                Path.Combine(modRoot, "Current", "Assemblies", "0Harmony.dll"),
                Path.Combine(modRoot, "1.6", "Assemblies", "0Harmony.dll"),
                Path.Combine(modRoot, "Assemblies", "0Harmony.dll")
            };
            string direct = preferred.FirstOrDefault(File.Exists);
            if (direct != null)
            {
                return direct;
            }

            return Directory.GetFiles(
                    modRoot,
                    "0Harmony.dll",
                    SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static bool IsHarmonyAssembly(Assembly assembly)
        {
            return string.Equals(
                assembly?.GetName().Name,
                HarmonyAssemblyName,
                StringComparison.Ordinal);
        }

        private static void Validate(Assembly harmony)
        {
            AssemblyName name = harmony.GetName();
            if (!string.Equals(
                    name.Name,
                    HarmonyAssemblyName,
                    StringComparison.Ordinal) ||
                name.Version == null ||
                name.Version.Major != 2 ||
                harmony.GetType("HarmonyLib.Harmony", throwOnError: false) == null ||
                harmony.GetType("HarmonyLib.HarmonyMethod", throwOnError: false) == null)
            {
                throw new NotSupportedException(
                    "The installed Harmony assembly is not compatible with " +
                    "FixWorld.Loader: " + name.FullName + ".");
            }
        }
    }
}
