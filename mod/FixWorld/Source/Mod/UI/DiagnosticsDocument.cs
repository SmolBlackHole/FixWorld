using System;
using System.Collections.Generic;

namespace FixWorld.UI
{
    internal sealed class DiagnosticsDocument
    {
        private DiagnosticsDocument(IReadOnlyList<DiagnosticsSection> sections)
        {
            Sections = sections;
        }

        internal IReadOnlyList<DiagnosticsSection> Sections { get; }

        internal static DiagnosticsDocument Parse(string text)
        {
            string source = string.IsNullOrWhiteSpace(text)
                ? "Status\n  No diagnostics are available."
                : text.Replace("\r\n", "\n").Replace('\r', '\n');
            List<DiagnosticsSection> sections =
                new List<DiagnosticsSection>();
            string title = null;
            List<string> lines = new List<string>();

            foreach (string rawLine in source.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                if (!char.IsWhiteSpace(rawLine[0]))
                {
                    AddSection(sections, title, lines);
                    title = rawLine.Trim();
                    lines = new List<string>();
                    continue;
                }

                lines.Add(rawLine.Trim());
            }

            AddSection(sections, title, lines);
            if (sections.Count == 1 && sections[0].Lines.Count == 0)
            {
                sections[0] = new DiagnosticsSection(
                    "Status",
                    new[] { sections[0].Title });
            }

            if (sections.Count == 0)
            {
                sections.Add(new DiagnosticsSection("Status", new[] { source }));
            }

            return new DiagnosticsDocument(sections.AsReadOnly());
        }

        internal int FindSection(string title)
        {
            for (int index = 0; index < Sections.Count; index++)
            {
                if (string.Equals(
                        Sections[index].Title,
                        title,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return 0;
        }

        private static void AddSection(
            ICollection<DiagnosticsSection> sections,
            string title,
            IReadOnlyList<string> lines)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            sections.Add(new DiagnosticsSection(title, lines));
        }
    }

    internal sealed class DiagnosticsSection
    {
        internal DiagnosticsSection(string title, IReadOnlyList<string> lines)
        {
            Title = title;
            Lines = lines;
        }

        internal string Title { get; }

        internal IReadOnlyList<string> Lines { get; }
    }
}
