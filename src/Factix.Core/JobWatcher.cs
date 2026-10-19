using System;
using System.IO;

namespace Factix
{
    public sealed class JobWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly object _gate = new object();
        private string _lastPath = "";
        private DateTime _lastAt = DateTime.MinValue;

        public JobWatcher(string directory)
        {
            Directory.CreateDirectory(directory);
            _watcher = new FileSystemWatcher(directory)
            {
                Filter = "job.json",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite
            };
            _watcher.Created += OnChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.EnableRaisingEvents = true;
        }

        public event EventHandler<string>? JobReady;

        public void Dispose()
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            Publish(e.FullPath);
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            Publish(e.FullPath);
        }

        private void Publish(string path)
        {
            if (!string.Equals(Path.GetFileName(path), "job.json", StringComparison.OrdinalIgnoreCase))
                return;

            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
                return;

            lock (_gate)
            {
                var now = DateTime.UtcNow;
                if (string.Equals(_lastPath, directory, StringComparison.OrdinalIgnoreCase) && (now - _lastAt).TotalSeconds < 1)
                    return;
                _lastPath = directory;
                _lastAt = now;
            }

            JobReady?.Invoke(this, directory);
        }
    }
}
