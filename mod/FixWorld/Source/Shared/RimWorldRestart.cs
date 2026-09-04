using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using FixWorld.Preloader;

namespace FixWorld.Processes
{
    internal static class RimWorldRestart
    {
        internal const string CommandName = "restart-after-exit";

        private const string EncodedPrefix = "b64:";
        private static int scheduled;

        internal static void Request(
            string helperPath,
            Action shutdown,
            Action<string> reportFailure)
        {
            if (shutdown == null)
            {
                throw new ArgumentNullException(nameof(shutdown));
            }

            if (reportFailure == null)
            {
                throw new ArgumentNullException(nameof(reportFailure));
            }

            if (Interlocked.CompareExchange(ref scheduled, 1, 0) != 0)
            {
                return;
            }

            try
            {
                string resolvedHelper = Path.GetFullPath(helperPath ??
                    throw new ArgumentNullException(nameof(helperPath)));
                if (!File.Exists(resolvedHelper))
                {
                    throw new FileNotFoundException(
                        "The FixWorld restart helper is missing.",
                        resolvedHelper);
                }

                string[] commandLine = Environment.GetCommandLineArgs();
                if (commandLine.Length == 0)
                {
                    throw new InvalidOperationException(
                        "The RimWorld command line is unavailable.");
                }

                List<string> helperArguments = new List<string>(
                    commandLine.Length + 3)
                {
                    CommandName,
                    Process.GetCurrentProcess().Id.ToString(
                        CultureInfo.InvariantCulture),
                    Encode(Environment.CurrentDirectory),
                    Encode(Path.GetFullPath(commandLine[0]))
                };
                for (int index = 1; index < commandLine.Length; index++)
                {
                    helperArguments.Add(Encode(commandLine[index]));
                }

                ProcessStartInfo start = new ProcessStartInfo(
                    resolvedHelper,
                    BuildCommandLine(helperArguments))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(resolvedHelper)
                };
                using (Process helper = Process.Start(start))
                {
                    if (helper == null)
                    {
                        throw new InvalidOperationException(
                            "The FixWorld restart helper did not start.");
                    }
                }

            }
            catch (Exception exception)
            {
                Volatile.Write(ref scheduled, 0);
                reportFailure(
                    "RimWorld could not restart cleanly. Close and reopen it " +
                    "manually: " + exception);
                return;
            }

            shutdown();
        }

        internal static void RunHelper(string[] arguments)
        {
            if (arguments == null || arguments.Length < 3)
            {
                throw new ArgumentException(
                    "restart-after-exit requires a parent process, working " +
                    "directory, and executable.");
            }

            if (!int.TryParse(
                    arguments[0],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int parentProcessId) ||
                parentProcessId <= 0 ||
                parentProcessId == Process.GetCurrentProcess().Id)
            {
                throw new ArgumentException("The parent process ID is invalid.");
            }

            string workingDirectory = Path.GetFullPath(Decode(arguments[1]));
            string executable = Path.GetFullPath(Decode(arguments[2]));
            if (!Directory.Exists(workingDirectory))
            {
                throw new DirectoryNotFoundException(
                    "The restart working directory does not exist: " +
                    workingDirectory);
            }

            if (!File.Exists(executable))
            {
                throw new FileNotFoundException(
                    "The restart executable does not exist.",
                    executable);
            }

            WaitForExit(parentProcessId);
            ClearInheritedLoaderState();

            List<string> childArguments = new List<string>(
                Math.Max(0, arguments.Length - 3));
            for (int index = 3; index < arguments.Length; index++)
            {
                childArguments.Add(Decode(arguments[index]));
            }

            ProcessStartInfo start = new ProcessStartInfo(
                executable,
                BuildCommandLine(childArguments))
            {
                UseShellExecute = false,
                WorkingDirectory = workingDirectory
            };
            using (Process child = Process.Start(start))
            {
                if (child == null)
                {
                    throw new InvalidOperationException(
                        "RimWorld did not restart.");
                }
            }
        }

        internal static string Encode(string value)
        {
            return EncodedPrefix + Convert.ToBase64String(
                Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        internal static string Decode(string value)
        {
            if (value == null || !value.StartsWith(
                    EncodedPrefix,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException("A restart argument is malformed.");
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(
                value.Substring(EncodedPrefix.Length)));
        }

        internal static string BuildCommandLine(IEnumerable<string> arguments)
        {
            if (arguments == null)
            {
                throw new ArgumentNullException(nameof(arguments));
            }

            StringBuilder commandLine = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (commandLine.Length > 0)
                {
                    commandLine.Append(' ');
                }

                AppendQuoted(commandLine, argument ?? string.Empty);
            }

            return commandLine.ToString();
        }

        private static void WaitForExit(int processId)
        {
            try
            {
                using (Process parent = Process.GetProcessById(processId))
                {
                    parent.WaitForExit();
                }
            }
            catch (ArgumentException)
            {
            }
        }

        private static void ClearInheritedLoaderState()
        {
            Environment.SetEnvironmentVariable(
                "DOORSTOP_INITIALIZED",
                null,
                EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(
                PreloaderTimelineContract.ActiveVariable,
                null,
                EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(
                PreloaderTimelineContract.RuntimeReadyVariable,
                null,
                EnvironmentVariableTarget.Process);
        }

        private static void AppendQuoted(StringBuilder target, string argument)
        {
            if (argument.Length > 0 &&
                argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                target.Append(argument);
                return;
            }

            target.Append('"');
            int backslashes = 0;
            foreach (char character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    target.Append('\\', backslashes * 2 + 1);
                    target.Append('"');
                    backslashes = 0;
                    continue;
                }

                target.Append('\\', backslashes);
                backslashes = 0;
                target.Append(character);
            }

            target.Append('\\', backslashes * 2);
            target.Append('"');
        }
    }
}
