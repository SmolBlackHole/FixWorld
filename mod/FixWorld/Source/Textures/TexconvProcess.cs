// SPDX-License-Identifier: MPL-2.0
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
            TexconvOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
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
                    options,
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
            TexconvOptions options,
            CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = BuildArguments(
                    outputDirectory,
                    fileListPath,
                    options),
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

                cancellationToken.ThrowIfCancellationRequested();
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
            TexconvOptions options)
        {
            StringBuilder arguments = new StringBuilder("-nologo");
            if (options.Overwrite)
            {
                arguments.Append(" -y");
            }

            if (options.SingleProcess)
            {
                arguments.Append(" --single-proc");
            }

            if (options.IgnoreSrgb)
            {
                arguments.Append(" --ignore-srgb");
            }

            if (options.FlipVertical)
            {
                arguments.Append(" -vflip");
            }

            if (options.GpuAdapter.HasValue)
            {
                arguments.Append(" -gpu ")
                    .Append(options.GpuAdapter.Value.ToString(
                        CultureInfo.InvariantCulture));
            }

            arguments.Append(" -f ")
                .Append(options.Format)
                .Append(" -m ")
                .Append(options.MipCount.ToString(CultureInfo.InvariantCulture))
                .Append(" -o ")
                .Append(Quote(outputDirectory))
                .Append(" --file-list ")
                .Append(Quote(fileListPath));
            return arguments.ToString();
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
                    process.WaitForExit(2000);
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
            var quoted = new StringBuilder("\"");
            int slashes = 0;
            foreach (char character in value)
            {
                if (character == '\\') { slashes++; continue; }
                quoted.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                quoted.Append(character);
                slashes = 0;
            }
            quoted.Append('\\', slashes * 2).Append('"');
            return quoted.ToString();
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

    internal sealed class TexconvOptions
    {
        internal TexconvOptions(
            string format,
            int mipCount,
            bool flipVertical = true,
            bool ignoreSrgb = true,
            bool overwrite = true,
            bool singleProcess = true,
            int? gpuAdapter = null)
        {
            if (string.IsNullOrWhiteSpace(format) ||
                format.Any(char.IsWhiteSpace))
            {
                throw new ArgumentException(
                    "A texconv format must be a single token.",
                    nameof(format));
            }

            if (mipCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(mipCount));
            }

            if (gpuAdapter < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(gpuAdapter));
            }

            Format = format;
            MipCount = mipCount;
            FlipVertical = flipVertical;
            IgnoreSrgb = ignoreSrgb;
            Overwrite = overwrite;
            SingleProcess = singleProcess;
            GpuAdapter = gpuAdapter;
        }

        internal string Format { get; }

        internal int MipCount { get; }

        internal bool FlipVertical { get; }

        internal bool IgnoreSrgb { get; }

        internal bool Overwrite { get; }

        internal bool SingleProcess { get; }

        internal int? GpuAdapter { get; }
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
