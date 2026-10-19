using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Factix;

namespace FactixPrinter
{
    public static class InvoicePrinter
    {
        public static void Print(InvoiceDraft draft)
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
                return;

            var layout = InvoiceLayout.Build(draft);
            const double scale = 96.0 / 72.0;
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(
                    Brushes.White,
                    null,
                    new Rect(0, 0, InvoiceLayout.PageWidth * scale, InvoiceLayout.PageHeight * scale));
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(190, 184, 172)), 0.8);
                foreach (var rule in layout.Rules)
                {
                    context.DrawLine(
                        pen,
                        new Point(rule.X1 * scale, rule.Y * scale),
                        new Point(rule.X2 * scale, rule.Y * scale));
                }

                foreach (var text in layout.Texts)
                {
                    if (string.IsNullOrEmpty(text.Text))
                        continue;
                    var typeface = new Typeface(
                        new FontFamily("Segoe UI"),
                        FontStyles.Normal,
                        text.Bold ? FontWeights.SemiBold : FontWeights.Normal,
                        FontStretches.Normal);
                    var formatted = new FormattedText(
                        text.Text,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        text.FontSize * scale,
                        Brushes.Black,
                        1.0);
                    context.DrawText(formatted, new Point(text.X * scale, (text.Baseline - text.FontSize) * scale));
                }
            }

            dialog.PrintVisual(visual, string.IsNullOrWhiteSpace(draft.Folio) ? "Factix" : "Factix " + draft.Folio);
        }
    }
}
