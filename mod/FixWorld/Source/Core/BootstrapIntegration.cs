// SPDX-License-Identifier: MPL-2.0
using System;
using FixWorld.Bootstrap;
using FixWorld.Telemetry;
using Verse;

namespace FixWorld.Core
{
    internal static class BootstrapIntegration
    {
        private static Installation installation;
        internal static InstallationState? LastInstallationState { get; private set; }
        internal static string MaintenanceError { get; private set; }
        internal static InstallationState RefreshInstallation()
        {
            if (installation == null)
                throw new InvalidOperationException("Bootstrap installation context is not available.");
            MaintenanceError = null;
            LastInstallationState = installation.Inspect();
            return LastInstallationState.Value;
        }

        internal static bool RequestMaintenance(InstallationAction action)
        {
            try
            {
                if (installation == null)
                    throw new InvalidOperationException("Bootstrap installation context is not available.");
                var maintenance = installation.CreateMaintenance(action);
                return Restart.Request(installation.Helper, Root.Shutdown, message =>
                {
                    MaintenanceError = message;
                    Log.Error("[FixWorld] " + message);
                }, maintenance);
            }
            catch (Exception error)
            {
                MaintenanceError = error.Message;
                Log.Error("[FixWorld] Doorstop maintenance refused: " + error);
                return false;
            }
        }
        private static readonly TelemetryContract<BootSnapshot> contract = new("fixworld.bootstrap", 1, (data, writer) =>
        {
            writer.Value("phase", data.Phase.ToString());
            writer.Value("failure", data.Failure);
        });
        private static TelemetryRegistration<BootSnapshot> telemetry;
        private sealed class BootSnapshot
        {
            internal BootSnapshot() { Phase = BootSession.Current.Phase; Failure = BootSession.Current.Failure; }
            internal BootPhase Phase { get; }
            internal string Failure { get; }
        }
        internal static bool PrepareAttachment(ModContentPack content)
        {
            try
            {
                installation = new Installation(BootEnvironment.GameRoot, content.RootDir);
                var session = BootSession.Current;
                var state = installation.Inspect();
                LastInstallationState = state;
                if (session.Phase == BootPhase.CoreReady || session.IsAttached)
                    return true;
                if (session.Phase != BootPhase.Cold)
                    throw new InvalidOperationException("Early bootstrap is not ready: " + session.Phase + " " + session.Failure);
                if (state.Status == InstallationStatus.Current || state.RestartPending)
                    throw new InvalidOperationException("Doorstop did not activate this launch. No automatic restart loop. " + state.Message);
                installation.Install();
                session.RestartPending();
                // Installation occurs inside mod construction, which may be on a
                // loading thread. Shutdown belongs to the normal main-thread boundary.
                LongEventHandler.ExecuteWhenFinished(RequestRestart);
                return false;
            }
            catch (Exception error)
            {
                BootSession.Current.Fail(error);
                Log.Error("[FixWorld] Bootstrap unavailable: " + error);
                return false;
            }
        }
        internal static void ConfirmAttachment()
        {
            installation.ConfirmAttached();
            Publish();
        }
        internal static void RegisterTelemetry(LibraryDiagnostics diagnostics)
        {
            telemetry = diagnostics.Store.Register(contract);
        }
        internal static void Publish() => telemetry?.Publish(new BootSnapshot());
        internal static void RequestRestart()
        {
            if (installation == null)
            { Log.Error("[FixWorld] Restart has no installation context."); return; }
            Restart.Request(installation.Helper, Root.Shutdown, message => Log.Error("[FixWorld] " + message));
        }
    }
}
