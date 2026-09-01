using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FixWorld.Runtime;
using Verse;

namespace FixWorld.Loading
{
    internal static class LoadingEstimateStore
    {
        private const string FileName = "loader-estimate-v1.txt";

        internal static double Read()
        {
            try
            {
                string path = GetPath();
                if (!File.Exists(path))
                {
                    return 0.0;
                }

                string[] lines = File.ReadAllLines(path);
                if (lines.Length != 2 ||
                    !string.Equals(lines[0], GetModListSignature(), StringComparison.Ordinal) ||
                    !double.TryParse(
                        lines[1],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double milliseconds) ||
                    milliseconds <= 0.0)
                {
                    return 0.0;
                }

                return milliseconds;
            }
            catch (Exception exception)
            {
                Log.Warning("[FixWorld] Could not read the loading estimate: " + exception.Message);
                return 0.0;
            }
        }

        internal static void Write(double milliseconds)
        {
            if (milliseconds <= 0.0)
            {
                return;
            }

            try
            {
                string path = GetPath();
                AtomicFile.WriteText(
                    path,
                    GetModListSignature() + Environment.NewLine +
                    milliseconds.ToString("R", CultureInfo.InvariantCulture),
                    new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Log.Warning("[FixWorld] Could not save the loading estimate: " + exception.Message);
            }
        }

        private static string GetPath()
        {
            return Path.Combine(GenFilePaths.SaveDataFolderPath, "FixWorld", FileName);
        }

        private static string GetModListSignature()
        {
            string modList = string.Join(
                "\n",
                LoadedModManager.RunningModsListForReading.Select(mod => mod.PackageId));
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(modList));
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }
    }
}
