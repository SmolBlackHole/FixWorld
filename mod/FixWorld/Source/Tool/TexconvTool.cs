using System;
using System.IO;
using System.Linq;
using System.Threading;
using FixWorld.ExternalTools;

namespace FixWorld.Tool
{
    internal static class TexconvTool
    {
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

            string executablePath = TexconvProcess.FindExecutable(
                AppDomain.CurrentDomain.BaseDirectory);
            if (string.IsNullOrEmpty(executablePath))
            {
                throw new FileNotFoundException(
                    "texconv was not found beside FixWorld.Tool.exe. Set " +
                    "FIXWORLD_TEXCONV_PATH to override it.");
            }

            TexconvProcessResult result = TexconvProcess.Run(
                executablePath,
                outputDirectory,
                inputPaths,
                mipCount: 0,
                CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(result.Output))
            {
                Console.Out.Write(result.Output);
            }
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                Console.Error.Write(result.Error);
            }

            return result.ExitCode;
        }

        internal static string Usage()
        {
            return "  FixWorld.Tool.exe texconv " +
                   "<output directory> <input path> [input path ...]";
        }
    }
}
