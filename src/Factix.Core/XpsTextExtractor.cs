using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml;

namespace Factix
{
    public static class XpsTextExtractor
    {
        public static bool IsPackage(byte[] data)
        {
            return data != null && data.Length > 4 && data[0] == (byte)'P' && data[1] == (byte)'K';
        }

        public static IReadOnlyList<TextPage> Read(byte[] data)
        {
            if (!IsPackage(data))
                return new TextPage[0];

            var pages = new List<TextPage>();
            using (var stream = new MemoryStream(data, writable: false))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false))
            {
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.IndexOf(".fpage", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    using (var entryStream = entry.Open())
                    {
                        var page = ReadPage(entryStream, pages.Count + 1);
                        if (page != null)
                            pages.Add(page);
                    }
                }
            }

            return pages;
        }

        private static TextPage? ReadPage(Stream stream, int pageNumber)
        {
            var document = new XmlDocument();
            document.Load(stream);
            var root = document.DocumentElement;
            var width = 612.0;
            var height = 792.0;
            if (root != null)
            {
                width = UnitsToPoints(root.GetAttribute("Width"), width);
                height = UnitsToPoints(root.GetAttribute("Height"), height);
            }

            var runs = new List<TextRun>();
            var nodes = document.SelectNodes("//*[local-name()='Glyphs']");
            if (nodes == null)
                return new TextPage(pageNumber, width, height, runs);

            foreach (XmlNode node in nodes)
            {
                if (!(node is XmlElement glyph))
                    continue;

                var text = Unescape(glyph.GetAttribute("UnicodeString"));
                if (string.IsNullOrEmpty(text))
                    continue;

                var em = Parse(glyph.GetAttribute("FontRenderingEmSize"), 12);
                var originX = Parse(glyph.GetAttribute("OriginX"), 0);
                var originY = Parse(glyph.GetAttribute("OriginY"), em);
                var emHeight = em * 72.0 / 96.0;
                var x = originX * 72.0 / 96.0;
                var baseline = originY * 72.0 / 96.0;
                var y = baseline - emHeight;
                var font = FontName(glyph.GetAttribute("FontUri"));
                var widthPt = text.Length * emHeight * 0.5;
                runs.Add(new TextRun(pageNumber, text, x, y, widthPt, emHeight, font));
            }

            return new TextPage(pageNumber, width, height, runs);
        }

        private static string Unescape(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Replace("{}", "{");
        }

        private static string FontName(string fontUri)
        {
            if (string.IsNullOrWhiteSpace(fontUri))
                return "Arial";
            var name = fontUri.Replace('\\', '/');
            var slash = name.LastIndexOf('/');
            if (slash >= 0)
                name = name.Substring(slash + 1);
            var dot = name.LastIndexOf('.');
            if (dot > 0)
                name = name.Substring(0, dot);
            return name;
        }

        private static double UnitsToPoints(string value, double fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : Parse(value, fallback) * 72.0 / 96.0;
        }

        private static double Parse(string value, double fallback)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }
    }
}
