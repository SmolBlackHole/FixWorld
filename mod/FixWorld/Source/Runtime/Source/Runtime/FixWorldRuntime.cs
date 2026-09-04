namespace FixWorld.Runtime
{
    public static class FixWorldRuntime
    {
        public const int ContractVersion = 4;

        public static void StartEarly()
        {
            RuntimeHost.StartEarly();
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
            RuntimeHost.AttachMod(attachment);
        }

        public static string GetDiagnosticsText()
        {
            return RuntimeHost.GetDiagnosticsText();
        }

        public static string ClearDdsCache()
        {
            return RuntimeHost.ClearDdsCache();
        }

        public static string RetryFailedDdsBuilds()
        {
            return RuntimeHost.RetryFailedDdsBuilds();
        }

        public static void Shutdown()
        {
            RuntimeHost.Shutdown();
        }
    }
}
