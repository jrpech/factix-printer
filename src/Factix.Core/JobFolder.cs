using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Factix
{
    public sealed class PrintJob
    {
        public PrintJob(string id, string documentName, string datatype, string sourcePath, string directory, DateTime createdUtc)
        {
            Id = id ?? "";
            DocumentName = documentName ?? "";
            Datatype = datatype ?? "";
            SourcePath = sourcePath ?? "";
            Directory = directory ?? "";
            CreatedUtc = createdUtc;
        }

        public string Id { get; }
        public string DocumentName { get; }
        public string Datatype { get; }
        public string SourcePath { get; }
        public string Directory { get; }
        public DateTime CreatedUtc { get; }
        public bool HasPdf => File.Exists(Path.Combine(Directory, "result.pdf"));
    }

    public static class JobFolder
    {
        public static PrintJob Load(string directory)
        {
            var jsonPath = Path.Combine(directory, "job.json");
            return Parse(File.ReadAllText(jsonPath, Encoding.UTF8), directory);
        }

        public static PrintJob Parse(string json, string directory)
        {
            var file = Required(json, "file");
            DateTime created;
            if (!DateTime.TryParse(Optional(json, "createdUtc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out created))
                created = DateTime.UtcNow;

            return new PrintJob(
                Optional(json, "id"),
                Optional(json, "documentName"),
                Optional(json, "datatype"),
                Path.Combine(directory, file),
                directory,
                created);
        }

        public static IReadOnlyList<PrintJob> List(string jobsDirectory)
        {
            var jobs = new List<PrintJob>();
            if (!Directory.Exists(jobsDirectory))
                return jobs;

            foreach (var directory in Directory.GetDirectories(jobsDirectory))
            {
                if (!File.Exists(Path.Combine(directory, "job.json")))
                    continue;
                try
                {
                    jobs.Add(Load(directory));
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            jobs.Sort((a, b) => b.CreatedUtc.CompareTo(a.CreatedUtc));
            return jobs;
        }

        public static string ReadString(string json, string key)
        {
            return Optional(json, key);
        }

        private static string Required(string json, string key)
        {
            var value = Optional(json, key);
            if (string.IsNullOrEmpty(value))
                throw new InvalidDataException("El trabajo no trae el campo " + key + ".");
            return value;
        }

        private static string Optional(string json, string key)
        {
            var marker = "\"" + key + "\"";
            var start = 0;
            while (start < json.Length)
            {
                var at = json.IndexOf(marker, start, StringComparison.Ordinal);
                if (at < 0)
                    return "";
                var cursor = at + marker.Length;
                while (cursor < json.Length && char.IsWhiteSpace(json[cursor]))
                    cursor++;
                if (cursor >= json.Length || json[cursor] != ':')
                {
                    start = at + marker.Length;
                    continue;
                }

                cursor++;
                while (cursor < json.Length && char.IsWhiteSpace(json[cursor]))
                    cursor++;
                if (cursor >= json.Length || json[cursor] != '"')
                    return "";
                cursor++;
                var builder = new StringBuilder();
                while (cursor < json.Length)
                {
                    var current = json[cursor++];
                    if (current == '"')
                        return builder.ToString();
                    if (current != '\\' || cursor >= json.Length)
                    {
                        builder.Append(current);
                        continue;
                    }

                    var escaped = json[cursor++];
                    switch (escaped)
                    {
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (cursor + 4 <= json.Length)
                            {
                                builder.Append((char)Convert.ToInt32(json.Substring(cursor, 4), 16));
                                cursor += 4;
                            }
                            break;
                        default:
                            builder.Append(escaped);
                            break;
                    }
                }

                return builder.ToString();
            }

            return "";
        }
    }
}
