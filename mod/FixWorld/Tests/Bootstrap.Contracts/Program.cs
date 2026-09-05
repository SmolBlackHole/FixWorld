using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml;
using FixWorld.Bootstrap;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); ++checks; }
    private static void Throws<T>(Action action, string name) where T : Exception
    {
        try
        { action(); }
        catch (T) { ++checks; return; }
        throw new Exception("Expected " + typeof(T).Name + ": " + name);
    }
    private static int Main(string[] args)
    {
        try
        {
            // A deliberately non-cooperating helper for the readiness-failure test.
            if (args.Length > 0 && int.TryParse(args[0], out _))
                return 2;
            if (args.Length > 0 && args[0] == "parent")
                return Parent(args);
            if (args.Length > 0 && (args[0] == "core" || args[0] == "entry" || args[0] == "disabled"))
            { Core(args); return 0; }
            if (args.Length != 1 && args.Length != 4)
                throw new ArgumentException("Pass helper path, optionally followed by FixWorld.dll, Harmony DLL and game Managed directory.");
            string root = Path.Combine(Path.GetTempPath(), "FixWorld-Bootstrap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Lifecycle();
                Config(root);
                InstallationTests(root);
                Processes(root, Path.GetFullPath(args[0]));
                if (args.Length == 4)
                    EntryFixtures(root, args);
            }
            finally { Directory.Delete(root, true); }
            Console.WriteLine($"PASS: {checks} bootstrap contracts, including real fixture process restarts. No RimWorld process started.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Lifecycle()
    {
        var core = typeof(Program).Assembly;
        var owner = new object();
        var session = new BootSession();
        int starts = 0, attaches = 0, completes = 0;
        Throws<InvalidOperationException>(() => session.Complete(() => { }), "cold cannot late-initialize");
        Check(session.Enter(true) && !session.Enter(true), "entry once");
        session.StartCore(core, () => ++starts);
        session.StartCore(core, () => ++starts);
        Check(starts == 1 && session.Phase == BootPhase.CoreReady, "core created once");
        Throws<InvalidOperationException>(() => session.Attach(typeof(string).Assembly, owner, () => { }), "assembly mismatch");
        session.Attach(core, owner, () => ++attaches);
        session.Attach(core, owner, () => ++attaches);
        Check(attaches == 1 && session.IsAttached, "attach once");
        Throws<InvalidOperationException>(() => session.Attach(core, new object(), () => { }), "different content owner");
        session.BeginCompletion(() => { });
        Check(session.Phase == BootPhase.Completing, "queued late init is not ready yet");
        session.Complete(() => ++completes);
        session.Complete(() => ++completes);
        Check(completes == 1 && session.Phase == BootPhase.Ready, "completion once");
        session.Stop();
        Check(!session.IsAttached, "stopped disables callbacks");
        session = new BootSession();
        Check(!session.Enter(false) && session.Phase == BootPhase.Disabled, "disabled entry");
        Throws<InvalidOperationException>(() => session.StartCore(core, () => { }), "disabled core cannot start");
        session = new BootSession();
        session.RestartPending();
        Throws<InvalidOperationException>(() => session.Complete(() => { }), "installation launch cannot complete");
        session = new BootSession();
        session.Enter(true);
        Throws<IOException>(() => session.StartCore(core, () => throw new IOException("fixture")), "failed core");
        Check(session.Phase == BootPhase.Failed && session.Failure.Contains("fixture"), "failure not marked ready");
        Throws<InvalidOperationException>(() => session.StartCore(core, () => ++starts), "failed initialization not repeated");
        session = new BootSession();
        session.Enter(true);
        session.StartCore(core, () => { });
        Throws<IOException>(() => session.Attach(core, owner, () => throw new IOException()), "failed attach");
        Check(!session.IsAttached, "failed attach does not publish attached");
        session = new BootSession();
        session.Enter(true);
        session.StartCore(core, () => { });
        session.Attach(core, owner, () => { });
        session.BeginCompletion(() => { });
        Throws<IOException>(() => session.Complete(() => throw new IOException()), "failed late initialization");
        Check(session.Phase == BootPhase.Failed, "failed late init does not mark ready");
        session = new BootSession();
        session.Enter(true);
        starts = 0;
        System.Threading.Tasks.Parallel.For(0, 16, _ => session.StartCore(core, () => ++starts));
        Check(starts == 1, "concurrent entry still creates one core");
        var initializing = new BootSession();
        initializing.Enter(true);
        using var entered = new ManualResetEvent(false);
        using var release = new ManualResetEvent(false);
        var work = System.Threading.Tasks.Task.Run(() => initializing.StartCore(core, () => { entered.Set(); release.WaitOne(3000); }));
        Check(entered.WaitOne(1000), "controlled initialization entered");
        bool readable;
        try
        {
            var read = System.Threading.Tasks.Task.Run(() => initializing.Failure);
            readable = read.Wait(1000);
        }
        finally { release.Set(); work.Wait(); }
        Check(readable, "telemetry status reads do not wait for initialization lock");
    }
    private static void Config(string root)
    {
        Check(!BootEnvironment.IsActive(root), "missing config inactive");
        Directory.CreateDirectory(Path.Combine(root, "Config"));
        string path = Path.Combine(root, "Config", "ModsConfig.xml");
        File.WriteAllText(path, "<ModsConfigData><activeMods><li> SMOLBLACKHOLE.FIXWORLD </li></activeMods></ModsConfigData>");
        Check(BootEnvironment.IsActive(root), "case/whitespace active");
        Check(BootEnvironment.SaveDataFolder(new[] { "game", "-savedatafolder=" + root }) == root, "equals save path");
        Check(BootEnvironment.SaveDataFolder(new[] { "game", "-savedatafolder", root }) == root, "separate save path");
        File.WriteAllText(path, "<ModsConfigData><activeMods><li>other</li></activeMods></ModsConfigData>");
        Check(!BootEnvironment.IsActive(root), "disabled mod");
        File.WriteAllText(path, "<!DOCTYPE x [<!ENTITY bad SYSTEM 'file:///not-read'>]><ModsConfigData>&bad;</ModsConfigData>");
        Throws<XmlException>(() => BootEnvironment.IsActive(root), "DTD prohibited");
    }
    private static void InstallationTests(string root)
    {
        string game = Path.Combine(root, "game"), bundle = Path.Combine(root, "bundle");
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(bundle);
        string proxy = Path.Combine(bundle, "winhttp.dll"), bootstrap = Path.Combine(bundle, "bootstrap.dll"), helper = Path.Combine(bundle, "helper.exe");
        File.WriteAllText(Path.Combine(game, "RimWorldWin64.exe"), "fixture");
        File.WriteAllText(proxy, "proxy fixture");
        File.WriteAllText(bootstrap, "bootstrap fixture");
        File.WriteAllText(helper, "helper fixture");
        var installation = new Installation(game, proxy, bootstrap, helper, Installation.Hash(proxy));
        Check(installation.Inspect().Status == InstallationStatus.Missing, "clean install");
        installation.Install();
        Check(installation.Inspect().Status == InstallationStatus.Current && installation.Inspect().RestartPending, "installed pending");
        Throws<InvalidOperationException>(installation.Install, "no restart loop");
        installation.ConfirmAttached();
        Check(!installation.Inspect().RestartPending, "confirmation clears pending");
        File.WriteAllText(bootstrap, "updated bootstrap");
        Check(installation.Inspect().Status == InstallationStatus.RepairRequired, "update detected");
        installation.Install();
        installation.ConfirmAttached();
        Check(installation.Inspect().Status == InstallationStatus.Current, "owned update repaired");
        File.Delete(Path.Combine(game, "doorstop_config.ini"));
        Check(installation.Inspect().Status == InstallationStatus.RepairRequired, "missing owned config");
        installation.Install();
        installation.ConfirmAttached();
        File.WriteAllText(Path.Combine(game, "doorstop_config.ini"), "foreign edited config");
        Check(installation.Inspect().Status == InstallationStatus.Conflict, "foreign config conflict");
        Throws<InvalidOperationException>(installation.Uninstall, "do not remove changed files");
        // Restore the test-owned config by saving its expected contents before tampering in a fresh fixture.
        File.Delete(Path.Combine(game, "doorstop_config.ini"));
        installation.Install();
        installation.ConfirmAttached();
        installation.Uninstall();
        installation.Install();
        installation.ConfirmAttached();
        byte[] priorConfig = File.ReadAllBytes(Path.Combine(game, "doorstop_config.ini"));
        string movedBootstrap = Path.Combine(bundle, "moved bootstrap.dll");
        File.Copy(bootstrap, movedBootstrap);
        var moved = new Installation(game, proxy, movedBootstrap, helper, Installation.Hash(proxy));
        moved.Install();
        File.WriteAllBytes(Path.Combine(game, "doorstop_config.ini"), priorConfig);
        Check(moved.Inspect().Status == InstallationStatus.RepairRequired && moved.Inspect().RestartPending,
            "interrupted repair retains ownership of the prior config");
        moved.Uninstall();
        Check(installation.Inspect().Status == InstallationStatus.Missing && File.Exists(bootstrap), "uninstall retains mod assembly");
        File.Copy(proxy, Path.Combine(game, "winhttp.dll"));
        Check(installation.Inspect().Status == InstallationStatus.Conflict, "matching proxy alone is not ownership");
        Throws<InvalidOperationException>(installation.Install, "foreign proxy refused");
        File.Delete(Path.Combine(game, "winhttp.dll"));
        File.WriteAllText(Path.Combine(game, "FixWorld.bootstrap.json"), "invalid json");
        Check(installation.Inspect().Status == InstallationStatus.Conflict, "corrupt manifest is not adopted");
        File.Delete(Path.Combine(game, "FixWorld.bootstrap.json"));
        installation.Install();
        File.Delete(Path.Combine(game, "doorstop_config.ini"));
        Check(installation.Inspect().RestartPending, "interrupted install retains attempt marker");
        Throws<InvalidOperationException>(installation.Install, "interrupted install does not loop");
        installation.Uninstall();
    }
    private static void Processes(string root, string helper)
    {
        foreach (string value in new[] { "", "plain", "two words", "quote\"here", "C:\\trailing slash\\", "ü漢字" })
            Check(Restart.Decode(Restart.Encode(value)) == value, "argument encoding round trip");
        bool shutdown = false;
        Check(!Restart.Request(Path.Combine(root, "missing.exe"), () => shutdown = true, _ => { }) && !shutdown, "failed helper must not shut down parent");
        Check(!Restart.Request(typeof(Program).Assembly.Location, () => shutdown = true, _ => { }) && !shutdown, "missing readiness acknowledgement prevents shutdown");
        Check(!Restart.Request(helper, () => throw new InvalidOperationException("shutdown fixture"), _ => { }), "shutdown exception cancels helper");
        string result = Path.Combine(root, "process");
        Directory.CreateDirectory(result);
        string[] arguments = { "parent", result, helper, "two words", "", "quoted \"value\"", "C:\\tail\\", "ü漢字" };
        using var parent = Process.Start(new ProcessStartInfo(typeof(Program).Assembly.Location, Restart.Quote(arguments))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = root
        });
        Check(parent.WaitForExit(15000) && parent.ExitCode == 0, "fixture parent shuts down");
        string output = Path.Combine(result, "child");
        Check(SpinWait.SpinUntil(() => File.Exists(output), 10000), "replacement starts after parent exit");
        string[] lines = File.ReadAllLines(output);
        Check(lines[0] == "parent-exited", "no overlapping game processes");
        Check(lines[1] == "clean", "inherited Doorstop state cleared");
        Check(lines[2] == root, "working directory preserved");
        Check(lines.Skip(3).SequenceEqual(arguments.Skip(3).Select(Restart.Encode)), "actual Windows command-line quoting preserved");
        Check(!File.Exists(Path.Combine(result, "duplicate")), "reentrant restart ignored");
        Check(File.ReadAllLines(Path.Combine(result, "launches")).Length == 1, "one replacement launch");
    }
    private static int Parent(string[] args)
    {
        string marker = Path.Combine(args[1], "marker"), child = Path.Combine(args[1], "child");
        if (File.Exists(marker))
        {
            int pid = int.Parse(File.ReadAllText(marker));
            bool alive;
            try
            { using var process = Process.GetProcessById(pid); alive = !process.HasExited; }
            catch (ArgumentException) { alive = false; }
            File.AppendAllText(Path.Combine(args[1], "launches"), "child\n");
            var lines = new[] { alive ? "parent-alive" : "parent-exited",
                Environment.GetEnvironmentVariable("DOORSTOP_INITIALIZED") == null ? "clean" : "dirty", Environment.CurrentDirectory }
                .Concat(args.Skip(3).Select(Restart.Encode));
            File.WriteAllLines(child + ".tmp", lines);
            File.Move(child + ".tmp", child);
            return 0;
        }
        File.WriteAllText(marker, Process.GetCurrentProcess().Id.ToString());
        Environment.SetEnvironmentVariable("DOORSTOP_INITIALIZED", "1");
        bool success = Restart.Request(args[2], () =>
        {
            Restart.Request(args[2], () => File.WriteAllText(Path.Combine(args[1], "duplicate"), "bad"), Console.Error.WriteLine);
            Thread.Sleep(250);
            Environment.Exit(0);
        }, Console.Error.WriteLine);
        return success ? 0 : 1;
    }
    private static void EntryFixtures(string root, string[] args)
    {
        string fixture = Path.Combine(root, "entry");
        Directory.CreateDirectory(Path.Combine(fixture, "Config"));
        string self = typeof(Program).Assembly.Location;
        string target = Path.Combine(fixture, Path.GetFileName(self));
        File.Copy(self, target);
        File.Copy(self + ".config", target + ".config");
        File.Copy(typeof(BootSession).Assembly.Location, Path.Combine(fixture, "FixWorld.Bootstrap.dll"));
        string core = Path.Combine(fixture, "FixWorld.dll");
        File.Copy(args[1], core);
        File.WriteAllText(Path.Combine(fixture, "Config", "ModsConfig.xml"),
            "<ModsConfigData><activeMods><li>smolblackhole.fixworld</li></activeMods></ModsConfigData>");
        foreach (string mode in new[] { "entry", "disabled" })
        {
            string save = mode == "entry" ? fixture : Path.Combine(root, "disabled");
            using var process = Process.Start(new ProcessStartInfo(target,
                Restart.Quote(new[] { mode, core, Path.GetFullPath(args[2]), Path.GetFullPath(args[3]), "-savedatafolder=" + save }))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = fixture
            });
            Check(process.WaitForExit(10000) && process.ExitCode == 0, "actual assembly entry fixture: " + mode);
        }
    }
    private static void Core(string[] args)
    {
        // Load real managed references, but never invoke a Unity/native operation.
        AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
        {
            string name = new AssemblyName(request.Name).Name;
            if (name == "0Harmony")
                return Assembly.LoadFrom(args[2]);
            string file = Path.Combine(args[3], name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        if (args[0] == "core")
            BootSession.Current.Enter(true);
        else
        {
            Doorstop.Entrypoint.Start();
            if (args[0] == "disabled")
            {
                Check(BootSession.Current.Phase == BootPhase.Disabled, "Doorstop disabled path");
                Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "FixWorld"), "disabled entry never loads core");
                Console.WriteLine("PASS: actual Doorstop entry remains inert when FixWorld is disabled.");
                return;
            }
            Check(BootSession.Current.Phase == BootPhase.Waiting, "entry waits for engine dependencies");
            Assembly.LoadFrom(Path.Combine(args[3], "Assembly-CSharp.dll"));
            Assembly.LoadFrom(args[2]);
            Check(BootSession.Current.Phase == BootPhase.CoreReady, "assembly-load event automatically starts early core");
        }
        var assembly = Assembly.LoadFrom(args[1]);
        var type = assembly.GetType("FixWorld.FixWorldController", true);
        var start = type.GetMethod("StartEarly");
        start.Invoke(null, null);
        object instance = type.GetProperty("Instance").GetValue(null);
        object diagnostics = type.GetProperty("Diagnostics").GetValue(instance);
        start.Invoke(null, null);
        Check(ReferenceEquals(Assembly.LoadFrom(args[1]), assembly), "normal load resolves same assembly");
        Check(ReferenceEquals(instance, type.GetProperty("Instance").GetValue(null)), "single controller");
        Check(ReferenceEquals(diagnostics, type.GetProperty("Diagnostics").GetValue(instance)), "single service graph");
        Check(BootSession.Current.Phase == BootPhase.CoreReady, "core ready without Unity initialization");
        type.GetMethod("DisposeCore", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
        Console.WriteLine("PASS: actual fork early core starts without native Unity calls and reuses its assembly/controller/services (desktop CLR).");
    }
}
