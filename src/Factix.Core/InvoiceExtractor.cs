using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Factix
{
    public static class InvoiceExtractor
    {
        private const string Missing = "No se encontró junto a la etiqueta.";
        private const string Unrecognized = "Formato no reconocido.";

        public static ExtractionResult Extract(TextDocument document)
        {
            var runs = new List<TextRun>(document.Runs);
            if (!MatchesSimpleInvoice(runs))
                return UnrecognizedResult();

            var used = new HashSet<int>();
            var draft = new InvoiceDraft();
            Assign(draft, runs, used, "Folio", new[] { "Folio" }, required: true);
            Assign(draft, runs, used, "Fecha", new[] { "Fecha" }, required: true);
            Assign(draft, runs, used, "Emisor", new[] { "Emisor" }, required: false);
            Assign(draft, runs, used, "RfcEmisor", new[] { "RFC Emisor" }, required: false);
            Assign(draft, runs, used, "Receptor", new[] { "Receptor" }, required: false);
            Assign(draft, runs, used, "RfcReceptor", new[] { "RFC Receptor" }, required: false);
            Assign(draft, runs, used, "Subtotal", new[] { "Subtotal" }, required: false);
            Assign(draft, runs, used, "Impuestos", new[] { "IVA", "Impuestos" }, required: false);
            Assign(draft, runs, used, "Total", new[] { "Total" }, required: true);
            draft.Conceptos = ReadLines(runs, out var linesWarning);
            var warnings = new List<string>();
            if (!string.IsNullOrEmpty(linesWarning))
            {
                warnings.Add(linesWarning);
                draft.LowConfidence["Conceptos"] = linesWarning;
            }

            return new ExtractionResult
            {
                TemplateName = "Factura simple",
                Recognized = true,
                Draft = draft,
                Warnings = warnings
            };
        }

        public static bool MatchesSimpleInvoice(IReadOnlyList<TextRun> runs)
        {
            return HasExact(runs, "factura") && HasExact(runs, "total");
        }

        private static ExtractionResult UnrecognizedResult()
        {
            var draft = new InvoiceDraft();
            foreach (var field in new[] { "Folio", "Fecha", "Emisor", "RfcEmisor", "Receptor", "RfcReceptor", "Subtotal", "Impuestos", "Total", "Conceptos" })
                draft.LowConfidence[field] = Unrecognized;

            return new ExtractionResult
            {
                Recognized = false,
                Draft = draft,
                Warnings = new List<string> { "No se reconoció el formato de la factura. Completa los campos a mano." }
            };
        }

        private static void Assign(InvoiceDraft draft, List<TextRun> runs, HashSet<int> used, string field, string[] labels, bool required)
        {
            foreach (var label in labels)
            {
                var index = FindLabel(runs, used, label);
                if (index < 0)
                    continue;

                used.Add(index);
                var valueIndex = FindValue(runs, used, runs[index]);
                if (valueIndex < 0)
                    break;

                used.Add(valueIndex);
                SetField(draft, field, runs[valueIndex].Text.Trim());
                return;
            }

            if (required || labels.Length > 0)
                draft.LowConfidence[field] = Missing;
        }

        private static int FindLabel(List<TextRun> runs, HashSet<int> used, string label)
        {
            var target = Fold(label);
            for (var i = 0; i < runs.Count; i++)
            {
                if (used.Contains(i))
                    continue;
                if (Fold(runs[i].Text) == target)
                    return i;
            }

            return -1;
        }

        private static int FindValue(List<TextRun> runs, HashSet<int> used, TextRun label)
        {
            var right = FindRight(runs, used, label);
            return right >= 0 ? right : FindBelow(runs, used, label);
        }

        private static readonly HashSet<string> LabelWords = new HashSet<string>
        {
            "factura", "folio", "fecha", "emisor", "rfc emisor", "receptor", "rfc receptor",
            "subtotal", "iva", "impuestos", "total", "descripcion", "cantidad", "precio", "importe"
        };

        private static bool IsLabel(TextRun run)
        {
            return LabelWords.Contains(Fold(run.Text));
        }

        private static int FindRight(List<TextRun> runs, HashSet<int> used, TextRun label)
        {
            var best = -1;
            var bestGap = double.MaxValue;
            for (var i = 0; i < runs.Count; i++)
            {
                if (used.Contains(i) || IsLabel(runs[i]) || runs[i].Page != label.Page || !SameLine(label, runs[i]))
                    continue;
                if (runs[i].X < label.Right - 2)
                    continue;
                var gap = runs[i].X - label.Right;
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = i;
                }
            }

            return best;
        }

        private static int FindBelow(List<TextRun> runs, HashSet<int> used, TextRun label)
        {
            var best = -1;
            var bestGap = double.MaxValue;
            for (var i = 0; i < runs.Count; i++)
            {
                if (used.Contains(i) || IsLabel(runs[i]) || runs[i].Page != label.Page)
                    continue;
                if (runs[i].Y <= label.Y + (label.Height * 0.5))
                    continue;
                if (runs[i].Y - label.Bottom > Math.Max(label.Height * 2.5, 28))
                    continue;
                var xDistance = Math.Abs(runs[i].X - label.X);
                var overlaps = runs[i].X < label.Right && runs[i].Right > label.X;
                if (!overlaps && xDistance > 40)
                    continue;
                var gap = runs[i].Y - label.Bottom;
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = i;
                }
            }

            return best;
        }

        private static bool SameLine(TextRun left, TextRun right)
        {
            var height = Math.Max(Math.Max(left.Height, right.Height), 1);
            var leftMid = left.Y + (left.Height / 2);
            var rightMid = right.Y + (right.Height / 2);
            return Math.Abs(leftMid - rightMid) <= height * 0.6;
        }

        private static List<InvoiceLineDraft> ReadLines(List<TextRun> runs, out string warning)
        {
            warning = "";
            var header = FindLoose(runs, "descripcion");
            if (header < 0)
            {
                warning = "No se encontró la tabla de partidas.";
                return new List<InvoiceLineDraft>();
            }

            var headerRun = runs[header];
            var columns = new List<Column>
            {
                new Column("Descripcion", headerRun.X)
            };
            AddColumn(columns, runs, headerRun, "cantidad", "Cantidad");
            AddColumn(columns, runs, headerRun, "precio", "Precio");
            AddColumn(columns, runs, headerRun, "importe", "Importe");
            columns.Sort((a, b) => a.X.CompareTo(b.X));

            var stop = double.MaxValue;
            foreach (var run in runs)
            {
                var name = Fold(run.Text);
                if (name != "subtotal" && name != "iva" && name != "impuestos" && name != "total")
                    continue;
                if (run.Page == headerRun.Page && run.Y > headerRun.Y + (headerRun.Height * 0.5) && run.Y < stop)
                    stop = run.Y;
            }

            var body = new List<TextRun>();
            foreach (var run in runs)
            {
                if (run.Page != headerRun.Page)
                    continue;
                if (run.Y <= headerRun.Y + (headerRun.Height * 0.4) || run.Y >= stop - 1)
                    continue;
                body.Add(run);
            }

            body.Sort((a, b) =>
            {
                var y = a.Y.CompareTo(b.Y);
                return y != 0 ? y : a.X.CompareTo(b.X);
            });

            var rows = new List<List<TextRun>>();
            var threshold = Math.Max(8, headerRun.Height * 0.8);
            foreach (var run in body)
            {
                if (rows.Count == 0 || run.Y - rows[rows.Count - 1][0].Y > threshold)
                    rows.Add(new List<TextRun>());
                rows[rows.Count - 1].Add(run);
            }

            var lines = new List<InvoiceLineDraft>();
            foreach (var row in rows)
            {
                var line = new InvoiceLineDraft();
                foreach (var run in row)
                {
                    var column = NearestColumn(columns, run.X);
                    Append(line, column, run.Text.Trim());
                }

                if (string.IsNullOrWhiteSpace(line.Descripcion)
                    && string.IsNullOrWhiteSpace(line.Cantidad)
                    && string.IsNullOrWhiteSpace(line.PrecioUnitario)
                    && string.IsNullOrWhiteSpace(line.Importe))
                    continue;
                lines.Add(line);
            }

            if (lines.Count == 0)
                warning = "No se reconocieron partidas.";
            return lines;
        }

        private static void AddColumn(List<Column> columns, List<TextRun> runs, TextRun header, string folded, string name)
        {
            foreach (var run in runs)
            {
                if (run.Page == header.Page && SameLine(header, run) && Fold(run.Text) == folded)
                {
                    columns.Add(new Column(name, run.X));
                    return;
                }
            }
        }

        private static int FindLoose(List<TextRun> runs, string folded)
        {
            for (var i = 0; i < runs.Count; i++)
            {
                if (Fold(runs[i].Text) == folded)
                    return i;
            }

            return -1;
        }

        private static string NearestColumn(List<Column> columns, double x)
        {
            var best = columns[0].Name;
            var bestDistance = double.MaxValue;
            foreach (var column in columns)
            {
                var distance = Math.Abs(column.X - x);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = column.Name;
                }
            }

            return best;
        }

        private static void Append(InvoiceLineDraft line, string column, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            switch (column)
            {
                case "Cantidad":
                    line.Cantidad = Join(line.Cantidad, text);
                    break;
                case "Precio":
                    line.PrecioUnitario = Join(line.PrecioUnitario, text);
                    break;
                case "Importe":
                    line.Importe = Join(line.Importe, text);
                    break;
                default:
                    line.Descripcion = Join(line.Descripcion, text);
                    break;
            }
        }

        private static string Join(string current, string addition)
        {
            return string.IsNullOrEmpty(current) ? addition : current + " " + addition;
        }

        private static bool HasExact(IReadOnlyList<TextRun> runs, string folded)
        {
            foreach (var run in runs)
            {
                if (Fold(run.Text) == folded)
                    return true;
            }

            return false;
        }

        public static string Fold(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var decomposed = value.Trim().TrimEnd(':').Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    builder.Append(char.ToLowerInvariant(ch));
            }

            return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
        }

        private static void SetField(InvoiceDraft draft, string field, string value)
        {
            switch (field)
            {
                case "Folio": draft.Folio = value; break;
                case "Fecha": draft.Fecha = value; break;
                case "Emisor": draft.Emisor = value; break;
                case "RfcEmisor": draft.RfcEmisor = value; break;
                case "Receptor": draft.Receptor = value; break;
                case "RfcReceptor": draft.RfcReceptor = value; break;
                case "Subtotal": draft.Subtotal = value; break;
                case "Impuestos": draft.Impuestos = value; break;
                case "Total": draft.Total = value; break;
            }
        }

        private sealed class Column
        {
            public Column(string name, double x)
            {
                Name = name;
                X = x;
            }

            public string Name { get; }
            public double X { get; }
        }
    }
}
