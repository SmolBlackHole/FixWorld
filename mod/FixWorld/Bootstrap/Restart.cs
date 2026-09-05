// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace FixWorld.Bootstrap
{
    public static class Restart
    {
        private static int requested;
        public static bool Request(string helperPath, Action shutdown, Action<string> reportError, InstallationMaintenance maintenance = null)
        {
            if (Interlocked.CompareExchange(ref requested, 1, 0) != 0)
                return true;
            string token = "Local\\FixWorldRestart-" + Guid.NewGuid().ToString("N");
            EventWaitHandle ready = null, commit = null, cancel = null;
            try
            {
                ready = new EventWaitHandle(false, EventResetMode.ManualReset, token + "-ready");
                commit = new EventWaitHandle(false, EventResetMode.ManualReset, token + "-commit");
                cancel = new EventWaitHandle(false, EventResetMode.ManualReset, token + "-cancel");
                using var parent = Process.GetCurrentProcess();
                var command = Environment.GetCommandLineArgs();
                var arguments = new List<string> { parent.Id.ToString(CultureInfo.InvariantCulture),
                    parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), token,
                    Encode(Environment.CurrentDirectory), Encode(parent.MainModule.FileName) };
                if (maintenance != null)
                {
                    maintenance.Validate();
                    arguments.Add("--maintenance");
                    arguments.Add(maintenance.Serialize());
                }
                for (int i = 1; i < command.Length; ++i)
                    arguments.Add(Encode(command[i]));
                using var helper = Process.Start(new ProcessStartInfo(Path.GetFullPath(helperPath), Quote(arguments))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(helperPath))
                });
                if (helper == null || !ready.WaitOne(5000) || helper.HasExited)
                    throw new InvalidOperationException("Restart helper did not confirm readiness.");
                commit.Set();
                shutdown();
                return true;
            }
            catch (Exception error)
            {
                cancel?.Set();
                Volatile.Write(ref requested, 0);
                reportError("Restart failed; the game was not forcibly terminated. Restart manually. " + error);
                return false;
            }
            finally { ready?.Dispose(); commit?.Dispose(); cancel?.Dispose(); }
        }

        public static void RunHelper(string[] arguments)
        {
            if (arguments.Length < 5)
                throw new ArgumentException("Missing restart handshake/launch arguments.");
            int parentId = int.Parse(arguments[0], CultureInfo.InvariantCulture);
            long started = long.Parse(arguments[1], CultureInfo.InvariantCulture);
            string token = arguments[2];
            if (!token.StartsWith("Local\\FixWorldRestart-", StringComparison.Ordinal))
                throw new ArgumentException("Invalid handshake name.");
            string directory = Path.GetFullPath(Decode(arguments[3]));
            string executable = Path.GetFullPath(Decode(arguments[4]));
            if (!Directory.Exists(directory) || !File.Exists(executable))
                throw new FileNotFoundException("Restart target or working directory missing.");
            var childArgs = new List<string>();
            InstallationMaintenance maintenance = null;
            int firstChildArgument = 5;
            if (arguments.Length > 5 && arguments[5] == "--maintenance")
            {
                if (arguments.Length < 7)
                    throw new ArgumentException("Missing installation maintenance request.");
                maintenance = InstallationMaintenance.Deserialize(arguments[6]);
                maintenance.Validate();
                firstChildArgument = 7;
            }
            for (int i = firstChildArgument; i < arguments.Length; ++i)
                childArgs.Add(Decode(arguments[i]));
            using var parent = Process.GetProcessById(parentId);
            if (parentId == Process.GetCurrentProcess().Id || parent.StartTime.ToUniversalTime().Ticks != started)
                throw new InvalidOperationException("Restart parent identity mismatch.");
            using var ready = EventWaitHandle.OpenExisting(token + "-ready");
            using var commit = EventWaitHandle.OpenExisting(token + "-commit");
            using var cancel = EventWaitHandle.OpenExisting(token + "-cancel");
            ready.Set();
            if (WaitHandle.WaitAny(new WaitHandle[] { cancel, commit }, 10000) != 1 || cancel.WaitOne(0))
                return;
            while (!parent.WaitForExit(100))
                if (cancel.WaitOne(0))
                    return;
            if (cancel.WaitOne(0))
                return;
            maintenance?.Execute();
            if (maintenance?.Action == InstallationAction.Uninstall)
                return;
            var start = new ProcessStartInfo(executable, Quote(childArgs)) { UseShellExecute = false, WorkingDirectory = directory };
            // Never inherit Doorstop's already-initialized flag into a fresh game.
            foreach (string key in new[] { "DOORSTOP_INITIALIZED", "FIXWORLD_PRELOADER_ACTIVE", "FIXWORLD_RUNTIME_READY" })
                start.EnvironmentVariables.Remove(key);
            using var child = Process.Start(start);
            if (child == null)
                throw new InvalidOperationException("Replacement process did not start.");
        }

        public static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        public static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
        public static string Quote(IEnumerable<string> arguments)
        {
            var result = new StringBuilder();
            foreach (var argument in arguments)
            {
                if (result.Length > 0)
                    result.Append(' ');
                result.Append('"');
                int slashes = 0;
                foreach (char character in argument)
                {
                    if (character == '\\')
                    { ++slashes; continue; }
                    result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                    result.Append(character);
                    slashes = 0;
                }
                result.Append('\\', slashes * 2).Append('"');
            }
            return result.ToString();
        }
    }
}
