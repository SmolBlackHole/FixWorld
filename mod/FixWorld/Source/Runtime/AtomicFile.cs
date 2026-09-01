using System;
using System.IO;
using System.Text;

namespace FixWorld.Runtime
{
    internal static class AtomicFile
    {
        internal static void Write(
            string path,
            Action<Stream> write,
            string backupPath = null)
        {
            if (write == null)
            {
                throw new ArgumentNullException(nameof(write));
            }

            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException(
                    "The output path has no parent directory: " + fullPath);
            }

            Directory.CreateDirectory(directory);
            string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    write(stream);
                    stream.Flush(true);
                }

                if (File.Exists(fullPath))
                {
                    File.Replace(temporaryPath, fullPath, backupPath);
                }
                else
                {
                    File.Move(temporaryPath, fullPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        internal static void WriteText(
            string path,
            string contents,
            Encoding encoding)
        {
            if (contents == null)
            {
                throw new ArgumentNullException(nameof(contents));
            }

            if (encoding == null)
            {
                throw new ArgumentNullException(nameof(encoding));
            }

            byte[] bytes = encoding.GetBytes(contents);
            Write(path, stream => stream.Write(bytes, 0, bytes.Length));
        }
    }
}
