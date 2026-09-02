using System;
using System.IO;
using System.Text;
using System.Xml;

namespace FixWorld.Content
{
    internal static class CombinedXmlCacheContract
    {
        internal const string EnabledEnvironmentVariable =
            "FIXWORLD_COMBINED_XML_PRELOAD";
        internal const string RootEnvironmentVariable =
            "FIXWORLD_COMBINED_XML_CACHE_ROOT";

        private const int Magic = 0x46575843;
        private const int SchemaVersion = 2;
        private const int MaximumNodes = 5_000_000;
        private const int MaximumStringBytes = 16 * 1024;
        private const int MaximumDocumentBytes = 512 * 1024 * 1024;
        private const long MaximumArtifactBytes =
            MaximumDocumentBytes + 128L * 1024L * 1024L;
        private const string FileName = "combined-xml-v1.fwxc";
        private const string CandidateKey = "FixWorld.CombinedXml.Candidate";
        private const string StopKey = "FixWorld.CombinedXml.Stop";

        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        internal static bool Enabled => !string.Equals(
            Environment.GetEnvironmentVariable(EnabledEnvironmentVariable),
            "0",
            StringComparison.Ordinal);

        internal static string GetPath(string saveDataFolder)
        {
            string root = Environment.GetEnvironmentVariable(
                RootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(saveDataFolder, "FixWorld", "Cache");
            }

            return Path.Combine(Path.GetFullPath(root), FileName);
        }

        internal static CombinedXmlArtifact Read(string path)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length <= 0L ||
                file.Length > MaximumArtifactBytes)
            {
                return null;
            }

            using (FileStream stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       64 * 1024,
                       FileOptions.SequentialScan))
            using (BinaryReader reader = new BinaryReader(stream, Utf8))
            {
                if (reader.ReadInt32() != Magic ||
                    reader.ReadInt32() != SchemaVersion)
                {
                    return null;
                }

                string identity = ReadString(reader);
                int nodeCount = ReadCount(reader, MaximumNodes);
                int[] nodeSources = new int[nodeCount];
                for (int index = 0; index < nodeCount; index++)
                {
                    nodeSources[index] = reader.ReadInt32();
                }

                int documentLength = ReadCount(reader, MaximumDocumentBytes);
                if (stream.Length - stream.Position != documentLength)
                {
                    return null;
                }

                return new CombinedXmlArtifact(
                    identity,
                    ReadDocument(stream),
                    nodeSources,
                    0.0);
            }
        }

        internal static void Write(
            Stream stream,
            string identity,
            int[] nodeSources,
            XmlDocument document)
        {
            using (BinaryWriter writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(Magic);
                writer.Write(SchemaVersion);
                WriteString(writer, identity);
                writer.Write(nodeSources.Length);
                foreach (int sourceIndex in nodeSources)
                {
                    writer.Write(sourceIndex);
                }

                writer.Flush();
            }

            long lengthPosition = stream.Position;
            using (BinaryWriter writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(0);
                writer.Flush();
            }

            long documentPosition = stream.Position;
            XmlWriterSettings settings = new XmlWriterSettings
            {
                CheckCharacters = false,
                CloseOutput = false,
                Encoding = new UTF8Encoding(false),
                Indent = false,
                NewLineHandling = NewLineHandling.None
            };
            using (XmlWriter writer = XmlWriter.Create(stream, settings))
            {
                document.Save(writer);
            }

            long endPosition = stream.Position;
            long documentLength = endPosition - documentPosition;
            if (documentLength < 0L || documentLength > MaximumDocumentBytes)
            {
                throw new InvalidDataException(
                    "Combined XML cache document is too large.");
            }

            stream.Position = lengthPosition;
            using (BinaryWriter writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write((int)documentLength);
                writer.Flush();
            }

            stream.Position = endPosition;
        }

        internal static void Publish(
            CombinedXmlArtifact artifact,
            double preloadMilliseconds)
        {
            if (artifact == null || IsStopRequested())
            {
                return;
            }

            AppDomain.CurrentDomain.SetData(
                CandidateKey,
                new object[]
                {
                    artifact.Identity,
                    artifact.Document,
                    artifact.NodeSources,
                    preloadMilliseconds
                });
        }

        internal static CombinedXmlArtifact TakePublished()
        {
            AppDomain.CurrentDomain.SetData(StopKey, true);
            object[] values = AppDomain.CurrentDomain.GetData(CandidateKey) as object[];
            AppDomain.CurrentDomain.SetData(CandidateKey, null);
            if (values == null || values.Length != 4)
            {
                return null;
            }

            return new CombinedXmlArtifact(
                values[0] as string,
                values[1] as XmlDocument,
                values[2] as int[],
                values[3] is double elapsed ? elapsed : 0.0);
        }

        internal static bool IsStopRequested()
        {
            return AppDomain.CurrentDomain.GetData(StopKey) is bool stopped &&
                   stopped;
        }

        private static XmlDocument ReadDocument(Stream stream)
        {
            XmlReaderSettings settings = new XmlReaderSettings
            {
                CheckCharacters = false,
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                XmlResolver = null
            };
            XmlDocument document = new XmlDocument { XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(stream, settings))
            {
                document.Load(reader);
            }

            return document;
        }

        private static int ReadCount(BinaryReader reader, int maximum)
        {
            int value = reader.ReadInt32();
            if (value < 0 || value > maximum)
            {
                throw new InvalidDataException("Invalid combined XML cache count.");
            }

            return value;
        }

        private static string ReadString(BinaryReader reader)
        {
            int length = ReadCount(reader, MaximumStringBytes);
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw new EndOfStreamException();
            }

            return Utf8.GetString(bytes);
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Utf8.GetBytes(value ?? string.Empty);
            if (bytes.Length > MaximumStringBytes)
            {
                throw new InvalidDataException("Combined XML cache string is too long.");
            }

            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
    }

    internal sealed class CombinedXmlArtifact
    {
        internal CombinedXmlArtifact(
            string identity,
            XmlDocument document,
            int[] nodeSources,
            double preloadMilliseconds)
        {
            Identity = identity;
            Document = document;
            NodeSources = nodeSources;
            PreloadMilliseconds = preloadMilliseconds;
        }

        internal string Identity { get; }
        internal XmlDocument Document { get; }
        internal int[] NodeSources { get; }
        internal double PreloadMilliseconds { get; }
    }
}
