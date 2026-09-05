using System;
using System.IO;

namespace FixWorld.Textures
{
    internal static class DdsIndexFile
    {
        internal static void Write(string path, Action<Stream> write, string backupPath)
        {
            string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew,
                           FileAccess.Write, FileShare.None))
                {
                    write(stream);
                    stream.Flush(true);
                }

                if (File.Exists(path)) File.Replace(temporaryPath, path, backupPath);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
