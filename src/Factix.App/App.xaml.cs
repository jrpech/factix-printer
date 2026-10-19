using System;
using System.Threading;
using System.Windows;

namespace FactixPrinter
{
    public partial class App : Application
    {
        private Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            var created = false;
            try
            {
                _mutex = new Mutex(true, @"Local\FactixPrinter", out created);
            }
            catch (AbandonedMutexException)
            {
                created = true;
            }
            catch (UnauthorizedAccessException)
            {
                created = true;
            }

            if (!created)
            {
                MessageBox.Show("Factix ya está abierto.", "Factix", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_mutex != null)
            {
                _mutex.Dispose();
                _mutex = null;
            }

            base.OnExit(e);
        }
    }
}
