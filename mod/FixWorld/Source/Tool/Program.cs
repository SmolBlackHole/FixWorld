using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FixWorld.Preloader;
using FixWorld.Processes;

namespace FixWorld.Tool
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    throw new ArgumentException(Usage());
                }

                switch (args[0].ToLowerInvariant())
                {
                    case RimWorldRestart.CommandName:
                        RimWorldRestart.RunHelper(
                            args.Skip(1).ToArray());
                        return 0;
                    case "preloader":
                        return RunPreloader(args.Skip(1).ToArray());
                    case "dds-cache":
                        return DdsCacheCleanup.Run(args.Skip(1).ToArray());
                    case "texconv":
                        return TexconvTool.Run(args.Skip(1).ToArray());
                    default:
                        throw new ArgumentException(Usage());
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("error: " + exception.Message);
                return 1;
            }
        }

        private static int RunPreloader(string[] args)
        {
            string command = args.Length > 0
                ? args[0].ToLowerInvariant()
                : "status";
            string gameRoot = args.Length > 1
                ? Path.GetFullPath(args[1])
                : FindGameRoot();
            if (args.Length > 2)
            {
                throw new ArgumentException(Usage());
            }

            PreloaderInstallationPaths paths = CreatePaths(gameRoot);
            switch (command)
            {
                case "status":
                    Console.WriteLine(
                        PreloaderInstallation.GetState(paths).Message);
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
                        "FixWorld preloader was removed. " +
                        "The normal mod is unchanged.");
                    return 0;
                default:
                    throw new ArgumentException(Usage());
            }
        }

        private static PreloaderInstallationPaths CreatePaths(string gameRoot)
        {
            string toolsRoot = AppDomain.CurrentDomain.BaseDirectory;
            return new PreloaderInstallationPaths(
                gameRoot,
                Path.Combine(
                    toolsRoot,
                    "Doorstop-4.4.0",
                    "winhttp.dll"),
                Path.Combine(toolsRoot, "FixWorld.Preloader.dll"));
        }

        private static string FindGameRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(
                AppDomain.CurrentDomain.BaseDirectory);
            for (int depth = 0;
                 depth < 6 && directory != null;
                 depth++, directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "RimWorldWin64.exe")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException(
                "Could not find RimWorldWin64.exe. Pass the RimWorld game " +
                "directory explicitly.");
        }

        internal static void RequireGameStopped()
        {
            if (Process.GetProcessesByName("RimWorldWin64").Any())
            {
                throw new InvalidOperationException(
                    "Close RimWorld before changing FixWorld files.");
            }
        }

        private static string Usage()
        {
            return "Usage:" + Environment.NewLine +
                   "  FixWorld.Tool.exe preloader " +
                   "[status|install|uninstall] [RimWorld game directory]" +
                   Environment.NewLine +
                   "  FixWorld.Tool.exe dds-cache " +
                   "[status|clean] [cache directory]" +
                   Environment.NewLine +
                   "  FixWorld.Tool.exe restart-after-exit " +
                   "<parent-pid> <encoded-working-directory> " +
                   "<encoded-executable> [encoded-arguments...]" +
                   Environment.NewLine +
                   TexconvTool.Usage();
        }
    }
}
