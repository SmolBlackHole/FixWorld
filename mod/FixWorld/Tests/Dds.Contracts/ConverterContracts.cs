using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using FixWorld.ExternalTools;

internal static class ConverterContracts
{
    private const string FixtureRoot = "FIXWORLD_DDS_TEST_CONVERTER_ROOT";
    private const string FixtureMode = "FIXWORLD_DDS_TEST_CONVERTER_MODE";

    internal static bool TryRunFixture(string[] args)
    {
        if (!args.Contains("-nologo")) return false;
        string root = Environment.GetEnvironmentVariable(FixtureRoot);
        File.WriteAllText(Path.Combine(root, "pid.txt"), Process.GetCurrentProcess().Id.ToString());
        File.WriteAllLines(Path.Combine(root, "args.txt"), args);
        int fileList = Array.IndexOf(args, "--file-list");
        File.Copy(args[fileList + 1], Path.Combine(root, "inputs.txt"), true);
        Console.WriteLine("fixture stdout");
        Console.Error.WriteLine("fixture stderr");
        string mode = Environment.GetEnvironmentVariable(FixtureMode);
        if (mode == "wait") Thread.Sleep(30000);
        if (mode == "fail") Environment.Exit(7);
        return true;
    }

    internal static void Run(string root, Action<bool, string> check)
    {
        string fixture = Path.Combine(root, "converter fixture");
        Directory.CreateDirectory(fixture);
        string source = Path.Combine(root, "input with spaces ü.png");
        File.WriteAllBytes(source, new byte[4]);
        string output = Path.Combine(root, "output with spaces") + Path.DirectorySeparatorChar;
        string executable = Assembly.GetExecutingAssembly().Location;
        string previousRoot = Environment.GetEnvironmentVariable(FixtureRoot);
        string previousMode = Environment.GetEnvironmentVariable(FixtureMode);
        try
        {
            Environment.SetEnvironmentVariable(FixtureRoot, fixture);
            Environment.SetEnvironmentVariable(FixtureMode, "success");
            TexconvOptions options = new TexconvOptions("BC7_UNORM", 3, gpuAdapter: 2);
            TexconvProcessResult result = TexconvProcess.Run(executable, output, new[] { source }, options, CancellationToken.None);
            string[] args = File.ReadAllLines(Path.Combine(fixture, "args.txt"));
            check(result.ExitCode == 0 && result.Output.Contains("fixture stdout") && result.Error.Contains("fixture stderr"), "converter exit and redirected output captured");
            check(args.Contains("-vflip") && args.Contains("--ignore-srgb") && args.Contains("--single-proc") && args.Contains("-y"), "BC7 production flags forwarded");
            check(args[Array.IndexOf(args, "-f") + 1] == "BC7_UNORM" && args[Array.IndexOf(args, "-m") + 1] == "3", "format and mip options forwarded");
            check(args[Array.IndexOf(args, "-gpu") + 1] == "2", "adapter option forwarded");
            check(args[Array.IndexOf(args, "-o") + 1] == output, "spaces and trailing backslash survive real Windows argument parser");
            check(File.ReadAllLines(Path.Combine(fixture, "inputs.txt")).Single() == source, "Unicode input list path preserved");
            check(!Directory.EnumerateFiles(output, ".texconv-*").Any(), "successful converter temporary file list removed");

            Environment.SetEnvironmentVariable(FixtureMode, "fail");
            result = TexconvProcess.Run(executable, output, new[] { source }, options, CancellationToken.None);
            check(result.ExitCode == 7 && result.Error.Contains("fixture stderr"), "nonzero converter exit retained");
            check(!Directory.EnumerateFiles(output, ".texconv-*").Any(), "failed converter temporary file list removed");

            File.Delete(Path.Combine(fixture, "pid.txt"));
            using (CancellationTokenSource cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                bool rejected = false;
                try { TexconvProcess.Run(executable, output, new[] { source }, options, cancelled.Token); }
                catch (OperationCanceledException) { rejected = true; }
                check(rejected && !File.Exists(Path.Combine(fixture, "pid.txt")), "pre-cancelled conversion never starts a child");
            }

            Environment.SetEnvironmentVariable(FixtureMode, "wait");
            using (CancellationTokenSource cancellation = new CancellationTokenSource(700))
            {
                Stopwatch elapsed = Stopwatch.StartNew();
                bool cancelled = false;
                try { TexconvProcess.Run(executable, output, new[] { source }, options, cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                check(cancelled && elapsed.Elapsed < TimeSpan.FromSeconds(6), "running converter cancellation has bounded completion");
                int childId = int.Parse(File.ReadAllText(Path.Combine(fixture, "pid.txt")));
                bool stopped;
                try { using (Process child = Process.GetProcessById(childId)) stopped = child.HasExited; }
                catch (ArgumentException) { stopped = true; }
                check(stopped, "cancelled converter child is stopped");
                check(!Directory.EnumerateFiles(output, ".texconv-*").Any(), "cancelled converter temporary file list removed");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(FixtureRoot, previousRoot);
            Environment.SetEnvironmentVariable(FixtureMode, previousMode);
        }
    }
}
