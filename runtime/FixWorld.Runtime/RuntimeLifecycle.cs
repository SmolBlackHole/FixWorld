using System;
using System.Threading;

namespace FixWorld.Runtime
{
    public enum FixWorldRuntimeState
    {
        NotStarted,
        Starting,
        EarlyReady,
        ModAttached,
        Running,
        Failed,
        Stopping,
        Stopped
    }

    public sealed class FixWorldRuntimeSnapshot
    {
        internal FixWorldRuntimeSnapshot(
            FixWorldRuntimeState state,
            bool hasAttachedMod,
            string failureMessage)
        {
            State = state;
            HasAttachedMod = hasAttachedMod;
            FailureMessage = failureMessage;
        }

        public FixWorldRuntimeState State { get; }

        public bool HasAttachedMod { get; }

        public string FailureMessage { get; }
    }

    internal sealed class RuntimeLifecycle
    {
        private readonly object sync = new object();

        private object attachedMod;
        private string failureMessage;
        private FixWorldRuntimeState state = FixWorldRuntimeState.NotStarted;

        internal FixWorldRuntimeSnapshot Snapshot
        {
            get
            {
                lock (sync)
                {
                    return CreateSnapshot();
                }
            }
        }

        internal void StartEarly(Action initialize)
        {
            if (initialize == null)
            {
                throw new ArgumentNullException(nameof(initialize));
            }

            lock (sync)
            {
                while (state == FixWorldRuntimeState.Starting)
                {
                    Monitor.Wait(sync);
                }

                if (state == FixWorldRuntimeState.EarlyReady ||
                    state == FixWorldRuntimeState.ModAttached ||
                    state == FixWorldRuntimeState.Running)
                {
                    return;
                }

                RequireState(FixWorldRuntimeState.NotStarted, "start");
                state = FixWorldRuntimeState.Starting;
            }

            try
            {
                initialize();
            }
            catch (Exception exception)
            {
                Fail(exception);
                throw;
            }

            lock (sync)
            {
                state = FixWorldRuntimeState.EarlyReady;
                Monitor.PulseAll(sync);
            }
        }

        internal void AttachMod(object mod, Action initialize)
        {
            if (mod == null)
            {
                throw new ArgumentNullException(nameof(mod));
            }

            if (initialize == null)
            {
                throw new ArgumentNullException(nameof(initialize));
            }

            lock (sync)
            {
                while (state == FixWorldRuntimeState.Starting ||
                       state == FixWorldRuntimeState.ModAttached)
                {
                    Monitor.Wait(sync);
                }

                if (state == FixWorldRuntimeState.Running)
                {
                    if (ReferenceEquals(attachedMod, mod))
                    {
                        return;
                    }

                    throw new InvalidOperationException(
                        "FixWorld.Runtime is already attached to a different mod instance.");
                }

                RequireState(FixWorldRuntimeState.EarlyReady, "attach the mod");
                attachedMod = mod;
                state = FixWorldRuntimeState.ModAttached;
            }

            try
            {
                initialize();
            }
            catch (Exception exception)
            {
                Fail(exception);
                throw;
            }

            lock (sync)
            {
                state = FixWorldRuntimeState.Running;
                Monitor.PulseAll(sync);
            }
        }

        internal void Shutdown(Action shutdown)
        {
            if (shutdown == null)
            {
                throw new ArgumentNullException(nameof(shutdown));
            }

            lock (sync)
            {
                while (state == FixWorldRuntimeState.Starting ||
                       state == FixWorldRuntimeState.ModAttached ||
                       state == FixWorldRuntimeState.Stopping)
                {
                    Monitor.Wait(sync);
                }

                if (state == FixWorldRuntimeState.Stopped ||
                    state == FixWorldRuntimeState.Failed)
                {
                    return;
                }

                if (state == FixWorldRuntimeState.NotStarted)
                {
                    state = FixWorldRuntimeState.Stopped;
                    Monitor.PulseAll(sync);
                    return;
                }

                if (state != FixWorldRuntimeState.EarlyReady &&
                    state != FixWorldRuntimeState.Running)
                {
                    throw InvalidState("shut down");
                }

                state = FixWorldRuntimeState.Stopping;
            }

            Exception failure = null;
            try
            {
                shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
                throw;
            }
            finally
            {
                lock (sync)
                {
                    if (failure != null)
                    {
                        failureMessage = FormatFailure(failure);
                    }

                    state = FixWorldRuntimeState.Stopped;
                    Monitor.PulseAll(sync);
                }
            }
        }

        private FixWorldRuntimeSnapshot CreateSnapshot()
        {
            return new FixWorldRuntimeSnapshot(
                state,
                attachedMod != null,
                failureMessage);
        }

        private void Fail(Exception exception)
        {
            lock (sync)
            {
                failureMessage = FormatFailure(exception);
                state = FixWorldRuntimeState.Failed;
                Monitor.PulseAll(sync);
            }
        }

        private void RequireState(
            FixWorldRuntimeState required,
            string operation)
        {
            if (state != required)
            {
                throw InvalidState(operation);
            }
        }

        private InvalidOperationException InvalidState(string operation)
        {
            return new InvalidOperationException(
                "Cannot " + operation + " FixWorld.Runtime while it is " +
                state + ".");
        }

        private static string FormatFailure(Exception exception)
        {
            return exception.GetType().FullName + ": " + exception.Message;
        }
    }
}
