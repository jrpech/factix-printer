using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Factix
{
    public static class InvoicePdfWriter
    {
        public static byte[] Write(InvoiceDraft draft)
        {
            var layout = InvoiceLayout.Build(draft);
            var content = BuildContent(layout);
            using (var stream = new MemoryStream())
            {
                var offsets = new List<long> { 0 };
                WriteAscii(stream, "%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
                offsets.Add(StartObject(stream, 1, "<< /Type /Catalog /Pages 2 0 R >>"));
                offsets.Add(StartObject(stream, 2, "<< /Type /Pages /Count 1 /Kids [3 0 R] >>"));
                offsets.Add(StartObject(stream, 3,
                    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> >>"));
                offsets.Add(WriteStreamObject(stream, 4, content));
                offsets.Add(StartObject(stream, 5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
                offsets.Add(StartObject(stream, 6, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));

                var xref = stream.Position;
                WriteAscii(stream, "xref\n0 7\n0000000000 65535 f \n");
                for (var i = 1; i <= 6; i++)
                    WriteAscii(stream, offsets[i].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
                WriteAscii(stream, "trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n");
                WriteAscii(stream, xref.ToString(CultureInfo.InvariantCulture));
                WriteAscii(stream, "\n%%EOF\n");
                return stream.ToArray();
            }
        }

        public static void WriteFile(InvoiceDraft draft, string path)
        {
            File.WriteAllBytes(path, Write(draft));
        }

        private static byte[] BuildContent(InvoiceLayout layout)
        {
            using (var stream = new MemoryStream())
            {
                foreach (var rule in layout.Rules)
                {
                    WriteAscii(stream, "0.75 G\n");
                    WriteAscii(stream, N(rule.X1) + " " + N(PdfY(rule.Y)) + " m " + N(rule.X2) + " " + N(PdfY(rule.Y)) + " l S\n");
                }

                foreach (var text in layout.Texts)
                {
                    if (string.IsNullOrEmpty(text.Text))
                        continue;
                    WriteAscii(stream, "BT\n");
                    WriteAscii(stream, (text.Bold ? "/F2 " : "/F1 ") + N(text.FontSize) + " Tf\n");
                    WriteAscii(stream, "0 G\n");
                    WriteAscii(stream, "1 0 0 1 " + N(text.X) + " " + N(PdfY(text.Baseline)) + " Tm\n");
                    WriteLiteral(stream, text.Text);
                    WriteAscii(stream, " Tj\nET\n");
                }

                return stream.ToArray();
            }
        }

        private static double PdfY(double yFromTop)
        {
            return InvoiceLayout.PageHeight - yFromTop;
        }

        private static long StartObject(Stream stream, int id, string body)
        {
            var offset = stream.Position;
            WriteAscii(stream, id.ToString(CultureInfo.InvariantCulture) + " 0 obj\n" + body + "\nendobj\n");
            return offset;
        }

        private static long WriteStreamObject(Stream stream, int id, byte[] content)
        {
            var offset = stream.Position;
            WriteAscii(stream, id.ToString(CultureInfo.InvariantCulture) + " 0 obj\n<< /Length " + content.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n");
            stream.Write(content, 0, content.Length);
            WriteAscii(stream, "\nendstream\nendobj\n");
            return offset;
        }

        private static void WriteLiteral(Stream stream, string text)
        {
            var bytes = Win1252.Encode(text);
            stream.WriteByte((byte)'(');
            foreach (var value in bytes)
            {
                if (value == (byte)'\\' || value == (byte)'(' || value == (byte)')')
                {
                    stream.WriteByte((byte)'\\');
                    stream.WriteByte(value);
                }
                else if (value < 32 || value > 126)
                {
                    var octal = "\\" + System.Convert.ToString(value, 8).PadLeft(3, '0');
                    WriteAscii(stream, octal);
                }
                else
                {
                    stream.WriteByte(value);
                }
            }

            stream.WriteByte((byte)')');
        }

        private static void WriteAscii(Stream stream, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string N(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
