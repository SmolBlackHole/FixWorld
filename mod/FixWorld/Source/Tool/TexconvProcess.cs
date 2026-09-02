using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace FixWorld.ExternalTools
{
    internal static class TexconvProcess
    {
        private const string ExecutableFileName = "texconv.exe";
        private const string PathEnvironmentVariable =
            "FIXWORLD_TEXCONV_PATH";
        private const int TimeoutMilliseconds = 10 * 60 * 1000;

        internal static string FindExecutable(string bundledDirectory)
        {
            string configuredPath = Environment.GetEnvironmentVariable(
                PathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredPath) &&
                File.Exists(configuredPath))
            {
                return Path.GetFullPath(configuredPath);
            }

            string executableName = IsWindows()
                ? ExecutableFileName
                : "texconv";
            if (!string.IsNullOrWhiteSpace(bundledDirectory))
            {
                string bundledPath = Path.Combine(
                    bundledDirectory,
                    executableName);
                if (File.Exists(bundledPath))
                {
                    return Path.GetFullPath(bundledPath);
                }
            }

            string path = Environment.GetEnvironmentVariable("PATH") ??
                          string.Empty;
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                string candidate = Path.Combine(
                    directory.Trim().Trim('"'),
                    executableName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            return IsWindows() ? FindWingetExecutable() : null;
        }

        internal static TexconvProcessResult Run(
            string executablePath,
            string outputDirectory,
            IReadOnlyList<string> inputPaths,
            int mipCount,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException(
                    "texconv was not found.",
                    executablePath);
            }

            if (inputPaths == null || inputPaths.Count == 0)
            {
                throw new ArgumentException(
                    "texconv requires at least one input path.",
                    nameof(inputPaths));
            }

            if (mipCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(mipCount));
            }

            string resolvedOutput = Path.GetFullPath(outputDirectory);
            string[] resolvedInputs = inputPaths
                .Select(Path.GetFullPath)
                .ToArray();
            foreach (string inputPath in resolvedInputs)
            {
                if (!File.Exists(inputPath))
                {
                    throw new FileNotFoundException(
                        "A texconv input file is missing.",
                        inputPath);
                }
            }

            Directory.CreateDirectory(resolvedOutput);
            string fileListPath = Path.Combine(
                resolvedOutput,
                ".texconv-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllLines(
                    fileListPath,
                    resolvedInputs,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return RunProcess(
                    Path.GetFullPath(executablePath),
                    resolvedOutput,
                    fileListPath,
                    mipCount,
                    cancellationToken);
            }
            finally
            {
                TryDelete(fileListPath);
            }
        }

        private static TexconvProcessResult RunProcess(
            string executablePath,
            string outputDirectory,
            string fileListPath,
            int mipCount,
            CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = BuildArguments(
                    outputDirectory,
                    fileListPath,
                    mipCount),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            StringBuilder output = new StringBuilder();
            StringBuilder error = new StringBuilder();
            using (Process process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (_, eventArgs) =>
                {
                    if (eventArgs.Data != null)
                    {
                        output.AppendLine(eventArgs.Data);
                    }
                };
                process.ErrorDataReceived += (_, eventArgs) =>
                {
                    if (eventArgs.Data != null)
                    {
                        error.AppendLine(eventArgs.Data);
                    }
                };

                process.Start();
                TryLowerPriority(process);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                Stopwatch timeout = Stopwatch.StartNew();
                while (!process.WaitForExit(100))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        TryKill(process);
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (timeout.ElapsedMilliseconds >= TimeoutMilliseconds)
                    {
                        TryKill(process);
                        throw new TimeoutException(
                            "texconv exceeded its ten minute time limit.");
                    }
                }

                process.WaitForExit();
                return new TexconvProcessResult(
                    process.ExitCode,
                    output.ToString(),
                    error.ToString());
            }
        }

        private static string BuildArguments(
            string outputDirectory,
            string fileListPath,
            int mipCount)
        {
            // RimWorld creates DDS textures with linear:false. Preserve the
            // encoded source values so Unity performs the single sRGB decode.
            return "-nologo -y --single-proc --ignore-srgb -vflip " +
                   "-f BC3_UNORM -m " +
                   mipCount.ToString(CultureInfo.InvariantCulture) +
                   " -o " + Quote(outputDirectory) +
                   " --file-list " + Quote(fileListPath);
        }

        private static void TryLowerPriority(Process process)
        {
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit();
                }
            }
            catch
            {
                // The process may exit between the state check and Kill().
            }
        }

        private static string FindWingetExecutable()
        {
            string localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            string packagesRoot = Path.Combine(
                localAppData,
                "Microsoft",
                "WinGet",
                "Packages");
            if (!Directory.Exists(packagesRoot))
            {
                return null;
            }

            try
            {
                return Directory
                    .EnumerateDirectories(
                        packagesRoot,
                        "Microsoft.DirectXTex.Texconv_*")
                    .Select(directory => Path.Combine(
                        directory,
                        ExecutableFileName))
                    .FirstOrDefault(File.Exists);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static bool IsWindows()
        {
            PlatformID platform = Environment.OSVersion.Platform;
            return platform == PlatformID.Win32NT ||
                   platform == PlatformID.Win32S ||
                   platform == PlatformID.Win32Windows ||
                   platform == PlatformID.WinCE;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
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
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    internal readonly struct TexconvProcessResult
    {
        internal TexconvProcessResult(
            int exitCode,
            string output,
            string error)
        {
            ExitCode = exitCode;
            Output = output;
            Error = error;
        }

        internal int ExitCode { get; }

        internal string Output { get; }

        internal string Error { get; }
    }
}
