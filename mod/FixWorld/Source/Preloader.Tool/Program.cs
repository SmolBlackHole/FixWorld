using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FixWorld.Preloader.Tool
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                string command = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
                string gameRoot = args.Length > 1
                    ? Path.GetFullPath(args[1])
                    : FindGameRoot();
                PreloaderInstallationPaths paths = CreatePaths(gameRoot);

                switch (command)
                {
                    case "status":
                        Console.WriteLine(PreloaderInstallation.GetState(paths).Message);
                        return 0;
                    case "install":
                        RequireGameStopped();
                        PreloaderInstallation.Install(paths);
                        Console.WriteLine("FixWorld preloader is installed.");
                        return 0;
                    case "uninstall":
                        RequireGameStopped();
                        PreloaderInstallation.Uninstall(paths);
                        Console.WriteLine(
                            "FixWorld preloader was removed. The normal mod is unchanged.");
                        return 0;
                    default:
                        throw new ArgumentException(
                            "Usage: FixWorld.Preloader.Tool.exe " +
                            "[status|install|uninstall] " +
                            "[RimWorld game directory]");
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("error: " + exception.Message);
                return 1;
            }
        }

        private static PreloaderInstallationPaths CreatePaths(string gameRoot)
        {
            string toolsRoot = AppDomain.CurrentDomain.BaseDirectory;
            return new PreloaderInstallationPaths(
                gameRoot,
                Path.Combine(toolsRoot, "Doorstop-4.4.0", "winhttp.dll"),
                Path.Combine(toolsRoot, "FixWorld.Preloader.dll"));
        }

        private static string FindGameRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int depth = 0; depth < 6 && directory != null; depth++, directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "RimWorldWin64.exe")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException(
                "Could not find RimWorldWin64.exe. Pass the RimWorld game directory explicitly.");
        }

        private static void RequireGameStopped()
        {
            if (Process.GetProcessesByName("RimWorldWin64").Any())
            {
                throw new InvalidOperationException(
                    "Close RimWorld before changing the preloader installation.");
            }
        }
    }
}
