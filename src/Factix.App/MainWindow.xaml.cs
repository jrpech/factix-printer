using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Factix;
using Microsoft.Win32;

namespace FactixPrinter
{
    public partial class MainWindow : Window
    {
        private readonly List<JobRow> _rows = new List<JobRow>();
        private JobWatcher? _watcher;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            FolderText.Text = FactixPaths.JobsDirectory;
            TryWatch();
            RefreshJobs();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _watcher?.Dispose();
        }

        private void TryWatch()
        {
            try
            {
                _watcher = new JobWatcher(FactixPaths.JobsDirectory);
                _watcher.JobReady += (_, directory) => Dispatcher.Invoke(() =>
                {
                    RefreshJobs();
                    OpenJob(directory);
                });
            }
            catch (Exception ex)
            {
                FolderText.Text = "No se puede vigilar " + FactixPaths.JobsDirectory + ". " + ex.Message;
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshJobs();
        }

        private void RefreshJobs()
        {
            _rows.Clear();
            foreach (var job in JobFolder.List(FactixPaths.JobsDirectory))
            {
                _rows.Add(new JobRow
                {
                    Directory = job.Directory,
                    DocumentName = string.IsNullOrWhiteSpace(job.DocumentName) ? job.Id : job.DocumentName,
                    Received = job.CreatedUtc.ToLocalTime().ToString("g"),
                    Status = job.HasPdf ? "PDF guardado" : "Pendiente"
                });
            }

            JobsList.ItemsSource = null;
            JobsList.ItemsSource = _rows;
            EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void JobsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (JobsList.SelectedItem is JobRow row)
                OpenJob(row.Directory);
        }

        private void OpenJob(string directory)
        {
            try
            {
                var job = JobFolder.Load(directory);
                if (!File.Exists(job.SourcePath))
                {
                    MessageBox.Show("El trabajo no tiene archivo de impresión.", "Factix", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                OpenDocument(TextNormalizer.ReadFile(job.SourcePath), job.DocumentName, job.Directory);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Factix", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Trabajos de impresión|*.xps;*.oxps;*.emf;*.spl;*.txt|Todos los archivos|*.*"
            };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                OpenDocument(TextNormalizer.ReadFile(dialog.FileName), Path.GetFileName(dialog.FileName), null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Factix", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSample_Click(object sender, RoutedEventArgs e)
        {
            var document = TextNormalizer.ReadBytes(SampleInvoice.CreateXps());
            OpenDocument(document, "Factura de ejemplo", null);
        }

        private void OpenDocument(TextDocument document, string title, string? jobDirectory)
        {
            var extraction = InvoiceExtractor.Extract(document);
            var review = new ReviewWindow(document, extraction, title, jobDirectory);
            review.Owner = this;
            review.Closed += delegate { RefreshJobs(); };
            review.Show();
        }

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            var setup = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Factix.Setup.exe");
            if (!File.Exists(setup))
            {
                MessageBox.Show(
                    "No está Factix.Setup.exe junto a la aplicación. En Windows, ejecuta build-all.bat y usa la carpeta artifacts\\Factix.",
                    "Factix",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(setup, "/install")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Factix", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private sealed class JobRow
        {
            public string Directory { get; set; } = "";
            public string DocumentName { get; set; } = "";
            public string Received { get; set; } = "";
            public string Status { get; set; } = "";
        }
    }
}
