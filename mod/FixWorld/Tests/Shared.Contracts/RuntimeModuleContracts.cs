using System;
using System.Collections.Generic;
using System.Threading;
using FixWorld.Runtime;
using FixWorld.Scheduling;
using FixWorld.Telemetry;

internal static class RuntimeModuleContracts
{
    internal static void Run(Action<bool, string> assert)
    {
        RegistrationAndPublication(assert);
        PublicationAndDisposalRace(assert);
        ModuleLifecycle(assert);
        InstallationFailures(assert);
        SharedServiceOwnership(assert);
    }

    private static void RegistrationAndPublication(Action<bool, string> assert)
    {
        using var store = new TelemetryStore();
        assert(Throws<ArgumentException>(() => store.Register<Data>(" ", 1)),
            "Empty telemetry ID was accepted.");
        assert(Throws<ArgumentOutOfRangeException>(() => store.Register<Data>("test", 0)),
            "Invalid schema version was accepted.");
        var first = store.Register<Data>("test", 1);
        var membership = store.Registrations;
        assert(first.Snapshot == null && first.SnapshotType == typeof(Data) &&
            first.SchemaVersion == 1, "Typed registration metadata is incorrect.");
        assert(Throws<InvalidOperationException>(() => store.Register<Data>("test", 2)),
            "Duplicate active ID was accepted.");
        assert(Throws<ArgumentNullException>(() => first.Publish(null)),
            "A null snapshot was accepted.");

        var snapshot = new Data(1);
        first.Publish(snapshot);
        assert(ReferenceEquals(snapshot, first.Snapshot) &&
            ReferenceEquals(snapshot, store.Registrations[0].PublishedSnapshot),
            "Publication copied the DTO or disagrees across typed/untyped views.");
        first.Publish(new Data(2));
        assert(snapshot.Value == 1 && first.Snapshot.Value == 2,
            "Publishing changed an older retained snapshot.");

        first.Dispose();
        first.Dispose();
        var replacement = store.Register<Data>("test", 2);
        first.Dispose();
        assert(store.Registrations.Count == 1 &&
            ReferenceEquals(store.Registrations[0], replacement) &&
            ReferenceEquals(membership[0], first),
            "Retired registration changed replacement membership or old membership view.");
        assert(Throws<ObjectDisposedException>(() => first.Publish(snapshot)),
            "A retired handle could still publish.");
        replacement.Publish(snapshot);
        store.Dispose();
        assert(store.Registrations.Count == 0 && ReferenceEquals(replacement.Snapshot, snapshot),
            "Store disposal lost a retained DTO or kept active registrations.");
        assert(Throws<ObjectDisposedException>(() => replacement.Publish(snapshot)) &&
            Throws<ObjectDisposedException>(() => store.Register<Data>("next", 1)),
            "Disposed store accepted publication or registration.");
    }

    private static void PublicationAndDisposalRace(Action<bool, string> assert)
    {
        using var store = new TelemetryStore();
        var registration = store.Register<Data>("race", 1);
        using var started = new ManualResetEventSlim();
        Exception failure = null;
        var producer = new Thread(() =>
        {
            try
            {
                registration.Publish(new Data(0));
                started.Set();
                for (int value = 1; value < 10000; value++)
                    registration.Publish(new Data(value));
            }
            catch (ObjectDisposedException) { }
            catch (Exception error) { failure = error; }
            finally { started.Set(); }
        });
        producer.Start();
        bool began = started.Wait(TimeSpan.FromSeconds(5));
        store.Dispose();
        var atDisposal = registration.Snapshot;
        bool joined = producer.Join(TimeSpan.FromSeconds(5));
        assert(began && joined && failure == null &&
            ReferenceEquals(atDisposal, registration.Snapshot),
            "Publication raced past disposal or failed unexpectedly.");
    }

    private static void ModuleLifecycle(Action<bool, string> assert)
    {
        using var services = CreateServices();
        var calls = new List<string>();
        var hooks = new Hooks(calls);
        using var module = new Module(services, calls, hooks);
        assert(module.PublishedSnapshot == null && !module.IsInstalled,
            "Uninstalled module already has published data.");
        module.Install();
        module.Install();
        assert(string.Join(",", calls) == "initialize,capture,install" &&
            module.IsInstalled && services.Telemetry.Registrations.Count == 1,
            "Module install order or idempotence is incorrect.");
        var old = module.PublishedSnapshot;
        module.Value = 7;
        module.Publish();
        assert(old.Value == 0 && module.PublishedSnapshot.Value == 7 &&
            ReferenceEquals(module.PublishedSnapshot,
                services.Telemetry.Registrations[0].PublishedSnapshot),
            "Module snapshot is not isolated and registered in the central store.");
        calls.Clear();
        module.Uninstall();
        module.Uninstall();
        assert(string.Join(",", calls) == "uninstall,shutdown" &&
            services.Telemetry.Registrations.Count == 0 && !module.IsInstalled,
            "Module cleanup order or idempotence is incorrect.");
        assert(Throws<InvalidOperationException>(module.Install) &&
            Throws<InvalidOperationException>(() => module.Publish()),
            "A retired module resumed without a new instance.");
        using var replacement = new Module(services, new List<string>());
        replacement.Install();
        assert(replacement.IsInstalled, "A replacement module could not register the released ID.");
    }

    private static void InstallationFailures(Action<bool, string> assert)
    {
        using var services = CreateServices();
        var calls = new List<string>();
        var hooks = new Hooks(calls) { FailInstall = true };
        using var failed = new Module(services, calls, hooks);
        assert(Throws<InvalidOperationException>(failed.Install) &&
            string.Join(",", calls) == "initialize,capture,install,uninstall,shutdown" &&
            services.Telemetry.Registrations.Count == 0,
            "Partial hook installation was not unwound.");

        calls.Clear();
        hooks = new Hooks(calls);
        using var badInit = new Module(services, calls, hooks) { FailInitialize = true };
        assert(Throws<InvalidOperationException>(badInit.Install) &&
            string.Join(",", calls) == "initialize,shutdown" &&
            services.Telemetry.Registrations.Count == 0,
            "Initialization failure leaked telemetry or attempted hooks.");

        calls.Clear();
        using var badCapture = new Module(services, calls, hooks) { FailCapture = true };
        assert(Throws<InvalidOperationException>(badCapture.Install) &&
            string.Join(",", calls) == "initialize,capture,shutdown" &&
            services.Telemetry.Registrations.Count == 0,
            "Initial snapshot failure leaked telemetry or installed hooks.");

        calls.Clear();
        hooks = new Hooks(calls) { FailUninstall = true };
        using var badCleanup = new Module(services, calls, hooks) { FailShutdown = true };
        badCleanup.Install();
        assert(Throws<AggregateException>(badCleanup.Uninstall) &&
            calls[calls.Count - 1] == "shutdown" &&
            services.Telemetry.Registrations.Count == 0,
            "Hook cleanup error prevented state and telemetry cleanup.");
    }

    private static void SharedServiceOwnership(Action<bool, string> assert)
    {
        using var services = CreateServices();
        services.Events.Register<int>(4);
        int observed = 0;
        using var subscription = services.Events.Subscribe<int>(value => observed += value);
        using var module = new Module(services, new List<string>());
        module.Install();
        module.Dispose();
        services.Events.Publish(2);
        services.Events.Pump();
        services.MainThread.BindCurrentThread();
        services.MainThread.Post("contract", () => observed++);
        services.MainThread.Pump(1, TimeSpan.FromSeconds(1));
        assert(observed == 3 && services.Scheduler.WorkerCount == 1,
            "Module disposal disposed borrowed runtime services.");
        services.MainThread.Post("discard", () => observed++);
        assert(services.Shutdown(TimeSpan.FromSeconds(2)) &&
            services.Shutdown(TimeSpan.FromSeconds(2)) && services.MainThread.PendingCount == 0,
            "Runtime services did not stop workers and discard pending actions idempotently.");
        assert(Throws<ObjectDisposedException>(() => services.Events.Publish(1)) &&
            Throws<ObjectDisposedException>(() => services.MainThread.Post("late", () => { })) &&
            Throws<ObjectDisposedException>(() => services.Telemetry.Register<Data>("late", 1)),
            "Shared services remained open after runtime shutdown.");
    }

    private static RuntimeServices CreateServices() => new(
        new JobSchedulerOptions(1, 1, 16, 1024), (_, __) => { });

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try
        { action(); }
        catch (TException) { return true; }
        return false;
    }

    private sealed class Data(int value)
    {
        internal int Value { get; } = value;
    }

    private sealed class Module(RuntimeServices services, List<string> calls,
        IRuntimeModuleHooks hooks = null) : RuntimeModule<Data>(services, "module", 1, hooks)
    {
        internal int Value;
        internal bool FailInitialize;
        internal bool FailCapture;
        internal bool FailShutdown;
        protected override void OnInitialize()
        {
            calls.Add("initialize");
            if (FailInitialize)
                throw new InvalidOperationException("initialize");
        }
        protected override Data CaptureSnapshot()
        {
            calls.Add("capture");
            if (FailCapture)
                throw new InvalidOperationException("capture");
            return new Data(Value);
        }
        protected override void OnShutdown()
        {
            calls.Add("shutdown");
            if (FailShutdown)
                throw new InvalidOperationException("shutdown");
        }
    }

    private sealed class Hooks(List<string> calls) : IRuntimeModuleHooks
    {
        internal bool FailInstall;
        internal bool FailUninstall;
        public void Install()
        {
            calls.Add("install");
            if (FailInstall)
                throw new InvalidOperationException("install");
        }
        public void Uninstall()
        {
            calls.Add("uninstall");
            if (FailUninstall)
                throw new InvalidOperationException("uninstall");
        }
    }
}
