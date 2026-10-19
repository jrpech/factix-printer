using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using Factix;

namespace FactixSetup
{
    internal static class PrinterInstaller
    {
        private const int PrinterAllAccess = 0x000F000C;
        private const uint PrinterAttributeLocal = 0x00000040;
        private const string ProcessorRegPath = @"SYSTEM\CurrentControlSet\Control\Print\Environments\Windows x64\Print Processors\" + FactixPaths.ProcessorName;
        private const string MonitorRegPath = @"SYSTEM\CurrentControlSet\Control\Print\Monitors\" + FactixPaths.MonitorName;

        public static void Install()
        {
            Ensure64Bit();
            var source = FindProcessorDll();
            EnsureDllIs64Bit(source);
            var processor = ProcessorPath();
            var monitor = MonitorDllPath();
            RestartSpooler(delegate
            {
                CopyDll(source, processor);
                CopyDll(source, monitor);
                RegisterProcessor();
                RegisterMonitor();
            });
            EnsurePortListed();
            RemovePrinterIfPresent();
            var driver = ChooseInstalledDriver();
            AddFactixPrinter(driver);
            EnsureJobsDirectory();
            Console.WriteLine("Impresora Factix instalada con el driver " + driver + ".");
            Console.WriteLine("Puerto: " + FactixPaths.PortName);
            Console.WriteLine("Monitor: " + monitor);
            Console.WriteLine("Los trabajos se guardan en " + FactixPaths.JobsDirectory);
            Console.WriteLine("Imprime a la impresora Factix. El botón Factura de ejemplo no pasa por la impresora.");
            Console.WriteLine("Para distribuir factixpp.dll, fírmala con Authenticode.");
        }

        public static void Uninstall()
        {
            Ensure64Bit();
            RemovePrinterIfPresent();
            RestartSpooler(delegate
            {
                UnregisterProcessor();
                UnregisterMonitor();
                using (new FileSystemNative())
                {
                    DeleteIfExists(ProcessorPath());
                    DeleteIfExists(MonitorDllPath());
                }
            });
            Console.WriteLine("Impresora Factix quitada. Los trabajos en " + FactixPaths.JobsDirectory + " se conservan.");
        }

        private static void Ensure64Bit()
        {
            if (!Environment.Is64BitOperatingSystem)
                throw new PlatformNotSupportedException("Factix requiere Windows de 64 bits.");
        }

        private static string FindProcessorDll()
        {
            var beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FactixPaths.ProcessorFileName);
            if (File.Exists(beside))
                return beside;

            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Factix.PrintProcessor", FactixPaths.ProcessorFileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            throw new FileNotFoundException("No se encontró factixpp.dll. Compílalo con src\\Factix.PrintProcessor\\build.bat y cópialo junto a Factix.Setup.exe.");
        }

        private static string WindowsRoot()
        {
            var windows = Environment.GetEnvironmentVariable("SystemRoot");
            return string.IsNullOrEmpty(windows) ? @"C:\Windows" : windows;
        }

        private static string ProcessorPath()
        {
            return Path.Combine(WindowsRoot(), "System32", "spool", "prtprocs", "x64", FactixPaths.ProcessorFileName);
        }

        private static string MonitorDllPath()
        {
            return Path.Combine(WindowsRoot(), "System32", FactixPaths.ProcessorFileName);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private static void CopyDll(string source, string destination)
        {
            using (new FileSystemNative())
            {
                var directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.Copy(source, destination, true);
                if (!File.Exists(destination))
                    throw new IOException("No se pudo copiar el procesador en " + destination + ".");
            }
        }

        private static void EnsureDllIs64Bit(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt16() != 0x5A4D)
                    throw new InvalidDataException("factixpp.dll no es un ejecutable de Windows.");
                stream.Position = 0x3C;
                var peOffset = reader.ReadInt32();
                stream.Position = peOffset;
                if (reader.ReadUInt32() != 0x00004550)
                    throw new InvalidDataException("factixpp.dll no tiene cabecera PE.");
                if (reader.ReadUInt16() != 0x8664)
                    throw new InvalidDataException("factixpp.dll tiene que ser de 64 bits. Vuelve a compilarla con build.bat.");
            }
        }

        private static void RegisterProcessor()
        {
            using (var key = Registry.LocalMachine.CreateSubKey(ProcessorRegPath))
            {
                if (key == null)
                    throw new InvalidOperationException("No se pudo registrar el procesador de impresión.");
                key.SetValue("Driver", FactixPaths.ProcessorFileName, RegistryValueKind.String);
            }
        }

        private static void UnregisterProcessor()
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(ProcessorRegPath, false);
            }
            catch (ArgumentException)
            {
            }
        }

        private static void RegisterMonitor()
        {
            using (var key = Registry.LocalMachine.CreateSubKey(MonitorRegPath))
            {
                if (key == null)
                    throw new InvalidOperationException("No se pudo registrar el monitor de puerto.");
                key.SetValue("Driver", FactixPaths.ProcessorFileName, RegistryValueKind.String);
            }
        }

        private static void UnregisterMonitor()
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(MonitorRegPath, false);
            }
            catch (ArgumentException)
            {
            }
        }

        private static void EnsurePortListed()
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (PortIsListed())
                    return;
                Thread.Sleep(500);
            }

            throw new InvalidOperationException(
                "El puerto " + FactixPaths.PortName + " no aparece. El monitor no cargó desde " + MonitorDllPath() + ".");
        }

        private static bool PortIsListed()
        {
            uint needed;
            uint returned;
            EnumPorts(null, 1, IntPtr.Zero, 0, out needed, out returned);
            if (needed == 0)
                return false;

            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!EnumPorts(null, 1, buffer, needed, out needed, out returned))
                    return false;
                var stride = IntPtr.Size;
                for (var i = 0; i < returned; i++)
                {
                    var namePtr = Marshal.ReadIntPtr(buffer, i * stride);
                    var name = Marshal.PtrToStringUni(namePtr);
                    if (string.Equals(name, FactixPaths.PortName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static void EnsureProcessorRegistered()
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (ListPrintProcessors().Any(name => string.Equals(name, FactixPaths.ProcessorName, StringComparison.OrdinalIgnoreCase)))
                    return;
                Thread.Sleep(500);
            }

            throw new InvalidOperationException(
                "El servicio de impresión no reconoce el procesador " + FactixPaths.ProcessorName +
                ". La DLL quedó en " + ProcessorPath() + ". Confirma que es de 64 bits y vuelve a instalar.");
        }

        private static List<string> ListPrintProcessors()
        {
            return ReadNameList(delegate(IntPtr buffer, uint size, out uint needed, out uint returned)
            {
                return EnumPrintProcessors(null, "Windows x64", 1, buffer, size, out needed, out returned);
            });
        }

        private static void RestartSpooler(Action whileStopped)
        {
            using (var controller = new ServiceController("Spooler"))
            {
                try
                {
                    if (controller.Status != ServiceControllerStatus.Stopped)
                    {
                        controller.Stop();
                        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(40));
                    }
                }
                catch (InvalidOperationException)
                {
                    controller.Refresh();
                    if (controller.Status != ServiceControllerStatus.Stopped)
                        throw;
                }

                whileStopped();
                controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(40));
            }
        }

        private static void RemovePrinterIfPresent()
        {
            var defaults = new PrinterDefaults { DesiredAccess = PrinterAllAccess };
            if (!OpenPrinterWithDefaults(FactixPaths.PrinterName, out var handle, ref defaults))
                return;

            try
            {
                if (!DeletePrinter(handle))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                ClosePrinter(handle);
            }

            Thread.Sleep(400);
        }

        private static string ChooseInstalledDriver()
        {
            var driver = PrinterDriverSelection.Choose(InstalledDrivers());
            if (string.IsNullOrEmpty(driver))
                throw new InvalidOperationException("No hay un driver de bandeja compatible. Hace falta Microsoft PS Class Driver, PCL6, XPS Document Writer o Generic / Text Only.");
            return driver!;
        }

        private static List<string> InstalledDrivers()
        {
            return ReadNameList(delegate(IntPtr buffer, uint size, out uint needed, out uint returned)
            {
                return EnumPrinterDrivers(null, "Windows x64", 1, buffer, size, out needed, out returned);
            });
        }

        private static List<string> ReadNameList(EnumNames enumerate)
        {
            uint needed;
            uint returned;
            enumerate(IntPtr.Zero, 0, out needed, out returned);
            if (needed == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!enumerate(buffer, needed, out needed, out returned))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                var names = new List<string>();
                for (var i = 0; i < returned; i++)
                {
                    var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, i * IntPtr.Size));
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }

                return names;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static void AddFactixPrinter(string driver)
        {
            var info = new PrinterInfo2
            {
                pPrinterName = FactixPaths.PrinterName,
                pPortName = FactixPaths.PortName,
                pDriverName = driver,
                pPrintProcessor = "winprint",
                pDatatype = "RAW",
                pComment = "Impresora virtual de Factix",
                Attributes = PrinterAttributeLocal,
                Priority = 1,
                DefaultPriority = 1
            };

            var handle = AddPrinter(null, 2, ref info);
            if (handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            ClosePrinter(handle);
        }

        private static void EnsureJobsDirectory()
        {
            var directory = Directory.CreateDirectory(FactixPaths.JobsDirectory);
            var security = directory.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.Modify | FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            directory.SetAccessControl(security);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PrinterDefaults
        {
            public string? pDatatype;
            public IntPtr pDevMode;
            public int DesiredAccess;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PrinterInfo2
        {
            public string? pServerName;
            public string? pPrinterName;
            public string? pShareName;
            public string? pPortName;
            public string? pDriverName;
            public string? pComment;
            public string? pLocation;
            public IntPtr pDevMode;
            public string? pSepFile;
            public string? pPrintProcessor;
            public string? pDatatype;
            public string? pParameters;
            public IntPtr pSecurityDescriptor;
            public uint Attributes;
            public uint Priority;
            public uint DefaultPriority;
            public uint StartTime;
            public uint UntilTime;
            public uint Status;
            public uint cJobs;
            public uint AveragePPM;
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool OpenPrinterWithDefaults(string pPrinterName, out IntPtr phPrinter, ref PrinterDefaults pDefault);

        [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "DeletePrinter", SetLastError = true)]
        private static extern bool DeletePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "AddPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AddPrinter(string? pName, uint level, ref PrinterInfo2 printer);

        private delegate bool EnumNames(IntPtr buffer, uint size, out uint needed, out uint returned);

        [DllImport("winspool.drv", EntryPoint = "EnumPrinterDriversW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumPrinterDrivers(string? pName, string pEnvironment, uint level, IntPtr pDriverInfo, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

        [DllImport("winspool.drv", EntryPoint = "EnumPrintProcessorsW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumPrintProcessors(string? pName, string pEnvironment, uint level, IntPtr pPrintProcessorInfo, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Wow64DisableWow64FsRedirection(out IntPtr oldValue);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Wow64RevertWow64FsRedirection(IntPtr oldValue);

        private sealed class FileSystemNative : IDisposable
        {
            private readonly IntPtr _cookie;
            private readonly bool _disabled;

            public FileSystemNative()
            {
                _disabled = Wow64DisableWow64FsRedirection(out _cookie);
            }

            public void Dispose()
            {
                if (_disabled)
                    Wow64RevertWow64FsRedirection(_cookie);
            }
        }

        [DllImport("winspool.drv", EntryPoint = "EnumPortsW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumPorts(string? pName, uint level, IntPtr pPorts, uint cbBuf, out uint pcbNeeded, out uint pcReturned);
    }
}
