using System.Collections.Generic;

namespace Factix
{
    public sealed class DrawText
    {
        public DrawText(string text, double x, double baseline, double fontSize, bool bold)
        {
            Text = text ?? "";
            X = x;
            Baseline = baseline;
            FontSize = fontSize;
            Bold = bold;
        }

        public string Text { get; }
        public double X { get; }
        public double Baseline { get; }
        public double FontSize { get; }
        public bool Bold { get; }
    }

    public sealed class DrawRule
    {
        public DrawRule(double x1, double y, double x2)
        {
            X1 = x1;
            Y = y;
            X2 = x2;
        }

        public double X1 { get; }
        public double Y { get; }
        public double X2 { get; }
    }

    public sealed class InvoiceLayout
    {
        public const double PageWidth = 612;
        public const double PageHeight = 792;

        public InvoiceLayout(IReadOnlyList<DrawText> texts, IReadOnlyList<DrawRule> rules)
        {
            Texts = texts;
            Rules = rules;
        }

        public IReadOnlyList<DrawText> Texts { get; }
        public IReadOnlyList<DrawRule> Rules { get; }

        public static InvoiceLayout Build(InvoiceDraft draft)
        {
            var texts = new List<DrawText>();
            var rules = new List<DrawRule>();
            texts.Add(new DrawText("FACTIX", 48, 64, 22, true));
            texts.Add(new DrawText("Factura", 48, 86, 11, false));
            rules.Add(new DrawRule(48, 100, 564));

            texts.Add(new DrawText("Folio", 48, 126, 9, false));
            texts.Add(new DrawText(draft.Folio, 110, 126, 12, true));
            texts.Add(new DrawText("Fecha", 330, 126, 9, false));
            texts.Add(new DrawText(draft.Fecha, 390, 126, 12, false));

            texts.Add(new DrawText("EMISOR", 48, 168, 9, true));
            texts.Add(new DrawText(draft.Emisor, 48, 186, 12, false));
            texts.Add(new DrawText(draft.RfcEmisor, 48, 204, 10, false));
            texts.Add(new DrawText("RECEPTOR", 320, 168, 9, true));
            texts.Add(new DrawText(draft.Receptor, 320, 186, 12, false));
            texts.Add(new DrawText(draft.RfcReceptor, 320, 204, 10, false));
            rules.Add(new DrawRule(48, 228, 564));

            texts.Add(new DrawText("Descripción", 48, 252, 9, true));
            texts.Add(new DrawText("Cantidad", 330, 252, 9, true));
            texts.Add(new DrawText("Precio", 410, 252, 9, true));
            texts.Add(new DrawText("Importe", 490, 252, 9, true));
            rules.Add(new DrawRule(48, 260, 564));

            var baseline = 278.0;
            foreach (var line in draft.Conceptos)
            {
                texts.Add(new DrawText(line.Descripcion, 48, baseline, 10, false));
                texts.Add(new DrawText(line.Cantidad, 330, baseline, 10, false));
                texts.Add(new DrawText(line.PrecioUnitario, 410, baseline, 10, false));
                texts.Add(new DrawText(line.Importe, 490, baseline, 10, false));
                baseline += 18;
            }

            baseline += 12;
            rules.Add(new DrawRule(320, baseline - 8, 564));
            texts.Add(new DrawText("Subtotal", 330, baseline + 8, 10, false));
            texts.Add(new DrawText(draft.Subtotal, 490, baseline + 8, 10, false));
            texts.Add(new DrawText("Impuestos", 330, baseline + 26, 10, false));
            texts.Add(new DrawText(draft.Impuestos, 490, baseline + 26, 10, false));
            texts.Add(new DrawText("Total", 330, baseline + 48, 12, true));
            texts.Add(new DrawText(draft.Total, 490, baseline + 48, 12, true));
            return new InvoiceLayout(texts, rules);
        }
    }
}
