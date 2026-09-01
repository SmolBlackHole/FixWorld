using System;

namespace FixWorld.Runtime
{
    public static class FixWorldRuntime
    {
        public const int ContractVersion = 1;

        private static readonly RuntimeLifecycle Lifecycle =
            new RuntimeLifecycle();

        public static FixWorldRuntimeSnapshot Snapshot => Lifecycle.Snapshot;

        public static void StartEarly()
        {
            Lifecycle.StartEarly(RuntimeHost.StartEarly);
        }

        public static void AttachMod(
            object mod,
            object content,
            float ddsCacheMaxGiB)
        {
            RuntimeModAttachmentSnapshot attachment =
                RuntimeModAttachmentSnapshot.Create(
                    mod,
                    content,
                    ddsCacheMaxGiB);
            Lifecycle.AttachMod(
                attachment.Mod,
                () => RuntimeHost.AttachMod(attachment));
        }

        public static void Shutdown()
        {
            Lifecycle.Shutdown(RuntimeHost.Shutdown);
        }

        internal static void Fail(Exception exception)
        {
            Lifecycle.MarkFailed(exception);
        }
    }
}
