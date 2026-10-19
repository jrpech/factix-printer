using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Factix;
using Microsoft.Win32;

namespace FactixPrinter
{
    public partial class ReviewWindow : Window
    {
        private readonly InvoiceDraft _draft;
        private readonly string? _jobDirectory;
        private readonly ObservableCollection<InvoiceLineDraft> _lines;

        public ReviewWindow(TextDocument document, ExtractionResult extraction, string title, string? jobDirectory)
        {
            InitializeComponent();
            Title = "Revisar — " + title;
            _draft = extraction.Draft;
            _jobDirectory = jobDirectory;
            _lines = new ObservableCollection<InvoiceLineDraft>(_draft.Conceptos);
            LinesGrid.ItemsSource = _lines;
            FolioBox.Text = _draft.Folio;
            FechaBox.Text = _draft.Fecha;
            EmisorBox.Text = _draft.Emisor;
            RfcEmisorBox.Text = _draft.RfcEmisor;
            ReceptorBox.Text = _draft.Receptor;
            RfcReceptorBox.Text = _draft.RfcReceptor;
            SubtotalBox.Text = _draft.Subtotal;
            ImpuestosBox.Text = _draft.Impuestos;
            TotalBox.Text = _draft.Total;
            WarningText.Text = string.Join(" ", extraction.Warnings);
            Mark(FolioBox, "Folio");
            Mark(FechaBox, "Fecha");
            Mark(EmisorBox, "Emisor");
            Mark(RfcEmisorBox, "RfcEmisor");
            Mark(ReceptorBox, "Receptor");
            Mark(RfcReceptorBox, "RfcReceptor");
            Mark(SubtotalBox, "Subtotal");
            Mark(ImpuestosBox, "Impuestos");
            Mark(TotalBox, "Total");
            if (_draft.LowConfidence.ContainsKey("Conceptos"))
                LinesGrid.BorderBrush = new SolidColorBrush(Color.FromRgb(243, 226, 184));
            DrawPreview(document);
        }

        private void Mark(TextBox box, string field)
        {
            if (!_draft.LowConfidence.TryGetValue(field, out var reason))
                return;
            box.Background = new SolidColorBrush(Color.FromRgb(243, 226, 184));
            box.ToolTip = reason;
        }

        private void DrawPreview(TextDocument document)
        {
            var pages = document.Pages.Count == 0
                ? new[] { new TextPage(1, 612, 792, new TextRun[0]) }
                : document.Pages;
            foreach (var page in pages)
            {
                var width = Math.Max(page.Width, page.Runs.Count == 0 ? page.Width : page.Runs.Max(run => run.Right) + 24);
                var height = Math.Max(page.Height, page.Runs.Count == 0 ? page.Height : page.Runs.Max(run => run.Bottom) + 24);
                var scale = width <= 0 ? 1 : Math.Min(96.0 / 72.0, 640.0 / width);
                var canvas = new Canvas
                {
                    Width = Math.Max(200, width * scale),
                    Height = Math.Max(200, height * scale),
                    Background = Brushes.White
                };
                foreach (var run in page.Runs)
                {
                    var block = new TextBlock
                    {
                        Text = run.Text,
                        FontSize = Math.Max(8, run.Height * scale),
                        Foreground = new SolidColorBrush(Color.FromRgb(28, 25, 21))
                    };
                    Canvas.SetLeft(block, run.X * scale);
                    Canvas.SetTop(block, run.Y * scale);
                    canvas.Children.Add(block);
                }

                PreviewHost.Children.Add(new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(221, 214, 200)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 0, 16),
                    Child = canvas
                });
            }
        }

        private void AddLine_Click(object sender, RoutedEventArgs e)
        {
            _lines.Add(new InvoiceLineDraft());
        }

        private void RemoveLine_Click(object sender, RoutedEventArgs e)
        {
            if (LinesGrid.SelectedItem is InvoiceLineDraft line)
                _lines.Remove(line);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ApplyForm();
            var dialog = new SaveFileDialog
            {
                Filter = "PDF|*.pdf",
                FileName = string.IsNullOrWhiteSpace(_draft.Folio) ? "factura.pdf" : "Factura-" + SafeName(_draft.Folio) + ".pdf"
            };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                InvoicePdfWriter.WriteFile(_draft, dialog.FileName);
                if (!string.IsNullOrEmpty(_jobDirectory))
                    InvoicePdfWriter.WriteFile(_draft, Path.Combine(_jobDirectory, "result.pdf"));
                MessageBox.Show("PDF guardado.", "Factix", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Factix", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            ApplyForm();
            try
            {
                InvoicePrinter.Print(_draft);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Factix", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyForm()
        {
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            LinesGrid.CommitEdit();
            _draft.Folio = FolioBox.Text.Trim();
            _draft.Fecha = FechaBox.Text.Trim();
            _draft.Emisor = EmisorBox.Text.Trim();
            _draft.RfcEmisor = RfcEmisorBox.Text.Trim();
            _draft.Receptor = ReceptorBox.Text.Trim();
            _draft.RfcReceptor = RfcReceptorBox.Text.Trim();
            _draft.Subtotal = SubtotalBox.Text.Trim();
            _draft.Impuestos = ImpuestosBox.Text.Trim();
            _draft.Total = TotalBox.Text.Trim();
            _draft.Conceptos = _lines.ToList();
        }

        private static string SafeName(string value)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '-');
            return value;
        }
    }
}
