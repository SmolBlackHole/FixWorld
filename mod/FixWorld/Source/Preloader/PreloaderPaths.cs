using System;
using System.Diagnostics;
using System.IO;

namespace FixWorld.Preloader
{
    internal static class PreloaderPaths
    {
        internal static string FindGameRoot()
        {
            string executablePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(executablePath))
            {
                throw new InvalidOperationException(
                    "Could not locate the RimWorld executable.");
            }

            return Path.GetDirectoryName(executablePath) ??
                   throw new DirectoryNotFoundException(
                       "Could not locate the RimWorld directory.");
        }

        internal static string FindSaveDataFolder()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length; index++)
            {
                const string prefix = "-savedatafolder=";
                if (arguments[index].StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(
                        arguments[index].Substring(prefix.Length));
                }

                if (string.Equals(
                        arguments[index],
                        "-savedatafolder",
                        StringComparison.OrdinalIgnoreCase) &&
                    index + 1 < arguments.Length)
                {
                    return Path.GetFullPath(arguments[index + 1]);
                }
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData",
                "LocalLow",
                "Ludeon Studios",
                "RimWorld by Ludeon Studios");
        }
    }
}
