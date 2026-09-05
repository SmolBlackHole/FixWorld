// SPDX-License-Identifier: MPL-2.0
using FixWorld.Utils;
using System.IO;
using Verse;

namespace FixWorld.Shell
{
    /// <summary>
    /// A Command to open the log file in the systems default text editor.
    /// </summary>
    public static class ShellOpenLog
    {
        public static bool Execute()
        {
            string logfile = FixWorldUtility.TryGetLogFilePath();
            if (logfile.NullOrEmpty() || !File.Exists(logfile))
            {
                FixWorldController.Logger.ReportException(new FileNotFoundException("Log file path is unknown or log file does not exist. Path:" + logfile));
                return false;
            }
            var platform = PlatformUtility.GetCurrentPlatform();
            switch (platform)
            {
                case PlatformType.Linux:
                    return Shell.StartProcess(new Shell.ShellCommand { FileName = logfile });
                case PlatformType.MacOSX:
                    return Shell.StartProcess(new Shell.ShellCommand { FileName = "open", Args = logfile });
                case PlatformType.Windows:
                    return Shell.StartProcess(new Shell.ShellCommand { FileName = logfile });
                default:
                    FixWorldController.Logger.ReportException(new Shell.UnsupportedPlatformException("ShellOpenLog"));
                    return false;
            }
        }
    }
}
