using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Factix
{
    public static class SampleInvoice
    {
        private static readonly GlyphSpec[] Glyphs =
        {
            new GlyphSpec("FACTURA", 40, 60, 22),
            new GlyphSpec("Folio", 40, 100, 12),
            new GlyphSpec("A-100", 160, 100, 12),
            new GlyphSpec("Fecha", 40, 130, 12),
            new GlyphSpec("2026-03-15", 160, 130, 12),
            new GlyphSpec("Emisor", 40, 170, 12),
            new GlyphSpec("Taller Norte SA", 160, 170, 12),
            new GlyphSpec("RFC Emisor", 40, 200, 12),
            new GlyphSpec("TNO010101AAA", 160, 200, 12),
            new GlyphSpec("Receptor", 40, 240, 12),
            new GlyphSpec("Cliente Sur SA", 160, 240, 12),
            new GlyphSpec("RFC Receptor", 40, 270, 12),
            new GlyphSpec("CSU020202BBB", 160, 270, 12),
            new GlyphSpec("Descripcion", 40, 320, 12),
            new GlyphSpec("Cantidad", 320, 320, 12),
            new GlyphSpec("Precio", 430, 320, 12),
            new GlyphSpec("Importe", 540, 320, 12),
            new GlyphSpec("Servicio de ajuste", 40, 360, 12),
            new GlyphSpec("2", 320, 360, 12),
            new GlyphSpec("150.00", 430, 360, 12),
            new GlyphSpec("300.00", 540, 360, 12),
            new GlyphSpec("Refacción filtro", 40, 390, 12),
            new GlyphSpec("1", 320, 390, 12),
            new GlyphSpec("80.50", 430, 390, 12),
            new GlyphSpec("80.50", 540, 390, 12),
            new GlyphSpec("Subtotal", 400, 450, 12),
            new GlyphSpec("380.50", 540, 450, 12),
            new GlyphSpec("IVA", 400, 480, 12),
            new GlyphSpec("60.88", 540, 480, 12),
            new GlyphSpec("Total", 400, 510, 12),
            new GlyphSpec("441.38", 540, 510, 12)
        };

        public static byte[] CreateXps()
        {
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    Write(zip, "[Content_Types].xml", ContentTypes);
                    Write(zip, "_rels/.rels", PackageRels);
                    Write(zip, "FixedDocumentSequence.fdseq", Sequence);
                    Write(zip, "Documents/1/FixedDocument.fdoc", Document);
                    Write(zip, "Documents/1/Pages/1.fpage", Page());
                }

                return stream.ToArray();
            }
        }

        public static string WriteXps(string path)
        {
            File.WriteAllBytes(path, CreateXps());
            return path;
        }

        private static void Write(ZipArchive zip, string name, string contents)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var entryStream = entry.Open())
            {
                var bytes = Encoding.UTF8.GetBytes(contents);
                entryStream.Write(bytes, 0, bytes.Length);
            }
        }

        private static string Page()
        {
            var builder = new StringBuilder();
            builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            builder.Append("<FixedPage xmlns=\"http://schemas.microsoft.com/xps/2005/06\" Width=\"816\" Height=\"1056\" xml:lang=\"es\">");
            foreach (var glyph in Glyphs)
            {
                builder.Append("<Glyphs UnicodeString=\"");
                builder.Append(Xml(glyph.Text));
                builder.Append("\" OriginX=\"");
                builder.Append(glyph.X.ToString("0.##", CultureInfo.InvariantCulture));
                builder.Append("\" OriginY=\"");
                builder.Append(glyph.Y.ToString("0.##", CultureInfo.InvariantCulture));
                builder.Append("\" FontRenderingEmSize=\"");
                builder.Append(glyph.Size.ToString("0.##", CultureInfo.InvariantCulture));
                builder.Append("\" FontUri=\"/Documents/1/Resources/Fonts/Arial.ttf\" />");
            }

            builder.Append("</FixedPage>");
            return builder.ToString();
        }

        private static string Xml(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;");
        }

        private const string ContentTypes =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"fdseq\" ContentType=\"application/vnd.ms-package.xps-fixeddocumentsequence+xml\"/>" +
            "<Default Extension=\"fdoc\" ContentType=\"application/vnd.ms-package.xps-fixeddocument+xml\"/>" +
            "<Default Extension=\"fpage\" ContentType=\"application/vnd.ms-package.xps-fixedpage+xml\"/>" +
            "</Types>";

        private const string PackageRels =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Type=\"http://schemas.microsoft.com/xps/2005/06/fixedrepresentation\" Target=\"/FixedDocumentSequence.fdseq\" Id=\"R1\"/>" +
            "</Relationships>";

        private const string Sequence =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<FixedDocumentSequence xmlns=\"http://schemas.microsoft.com/xps/2005/06\">" +
            "<DocumentReference Source=\"Documents/1/FixedDocument.fdoc\"/>" +
            "</FixedDocumentSequence>";

        private const string Document =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<FixedDocument xmlns=\"http://schemas.microsoft.com/xps/2005/06\">" +
            "<PageContent Source=\"Pages/1.fpage\"/>" +
            "</FixedDocument>";

        private readonly struct GlyphSpec
        {
            public GlyphSpec(string text, double x, double y, double size)
            {
                Text = text;
                X = x;
                Y = y;
                Size = size;
            }

            public string Text { get; }
            public double X { get; }
            public double Y { get; }
            public double Size { get; }
        }
    }
}
