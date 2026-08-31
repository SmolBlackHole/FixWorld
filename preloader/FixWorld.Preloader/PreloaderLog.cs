using System;
using System.IO;

namespace FixWorld.Preloader
{
    internal sealed class PreloaderLog
    {
        private readonly string path;

        internal PreloaderLog(string path)
        {
            this.path = path;
        }

        internal void Write(string message)
        {
            try
            {
                File.AppendAllText(
                    path,
                    DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
            }
            catch
            {
                Console.WriteLine("[FixWorld.Preloader] " + message);
            }
        }
    }
}
