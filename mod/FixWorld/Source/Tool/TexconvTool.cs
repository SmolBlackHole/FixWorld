using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace FixWorld.Tool
{
    internal static class TexconvTool
    {
        private const string ExecutableFileName = "texconv.exe";
        private const string PathEnvironmentVariable =
            "FIXWORLD_TEXCONV_PATH";

        internal static int Run(string[] args)
        {
            if (args == null || args.Length < 2)
            {
                throw new ArgumentException(Usage());
            }

            string outputDirectory = Path.GetFullPath(args[0]);
            string[] inputPaths = [.. args
                .Skip(1)
                .Select(Path.GetFullPath)];
            foreach (string inputPath in inputPaths)
            {
                if (!File.Exists(inputPath))
                {
                    throw new FileNotFoundException(
                        "A texconv input file is missing.",
                        inputPath);
                }
            }

            string executablePath = FindExecutable();
            Directory.CreateDirectory(outputDirectory);
            string fileListPath = Path.Combine(
                outputDirectory,
                ".texconv-" + Guid.NewGuid().ToString("N") + ".txt");

            try
            {
                File.WriteAllLines(
                    fileListPath,
                    inputPaths,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                using (Process process = Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = BuildArguments(outputDirectory, fileListPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                }))
                {
                    if (process == null)
                    {
                        throw new InvalidOperationException(
                            "Could not start texconv.");
                    }

                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            finally
            {
                TryDelete(fileListPath);
            }
        }

        internal static string Usage()
        {
            return "  FixWorld.Tool.exe texconv " +
                   "<output directory> <input path> [input path ...]";
        }

        private static string FindExecutable()
        {
            string configuredPath = Environment.GetEnvironmentVariable(
                PathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredPath) &&
                File.Exists(configuredPath))
            {
                return Path.GetFullPath(configuredPath);
            }

            string bundledPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                ExecutableFileName);
            if (File.Exists(bundledPath))
            {
                return bundledPath;
            }

            throw new FileNotFoundException(
                "texconv was not found beside FixWorld.Tool.exe. Set " +
                PathEnvironmentVariable + " to override it.",
                bundledPath);
        }

        private static string BuildArguments(
            string outputDirectory,
            string fileListPath)
        {
            return "-nologo -y --ignore-srgb -vflip -f BC3_UNORM -m 0 " +
                   "-o " + Quote(outputDirectory) +
                   " --file-list " + Quote(fileListPath);
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
}
