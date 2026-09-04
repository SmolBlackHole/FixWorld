using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace FixWorld.Preloader
{
    internal static class ActiveModConfig
    {
        internal const string FixWorldPackageId = "smolblackhole.fixworld";

        internal static bool IsFixWorldActive(string saveDataFolder)
        {
            return ReadLoadOrder(saveDataFolder).ContainsKey(FixWorldPackageId);
        }

        internal static IReadOnlyDictionary<string, int> ReadLoadOrder(
            string saveDataFolder)
        {
            Dictionary<string, int> result =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(saveDataFolder))
            {
                return result;
            }

            string path = Path.Combine(saveDataFolder, "Config", "ModsConfig.xml");
            if (!File.Exists(path))
            {
                return result;
            }

            try
            {
                XmlReaderSettings settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                XmlDocument document = new XmlDocument { XmlResolver = null };
                using (XmlReader reader = XmlReader.Create(path, settings))
                {
                    document.Load(reader);
                }

                XmlNodeList nodes = document.SelectNodes(
                    "/ModsConfigData/activeMods/li");
                if (nodes == null)
                {
                    return result;
                }

                foreach (XmlNode node in nodes)
                {
                    string packageId = node.InnerText?.Trim();
                    if (!string.IsNullOrEmpty(packageId) &&
                        !result.ContainsKey(packageId))
                    {
                        result.Add(packageId, result.Count);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (XmlException)
            {
            }

            return result;
        }
    }
}
