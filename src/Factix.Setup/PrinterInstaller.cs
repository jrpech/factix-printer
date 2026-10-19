using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Factix;

namespace FactixSetup
{
    internal static class PrinterInstaller
    {
        private const int PrinterAllAccess = 0x000F000C;
        private const int ServerAllAccess = 0x000F0003;
        private const uint PrinterAttributeLocal = 0x00000040;

        public static void Install()
        {
            Ensure64Bit();
            var source = FindProcessorDll();
            RestartSpooler(delegate { CopyDll(source, ProcessorPath()); });
            RemovePrinterIfPresent();
            var driver = ChooseInstalledDriver();
            EnsureNulPort();
            AddFactixPrinter(driver, PrinterDriverSelection.DatatypeFor(driver));
            EnsureJobsDirectory();
            Console.WriteLine("Impresora Factix instalada con el driver " + driver + ".");
            Console.WriteLine("Los trabajos se guardan en " + FactixPaths.JobsDirectory);
            Console.WriteLine("Para distribuir factixpp.dll, fírmala con Authenticode.");
        }

        public static void Uninstall()
        {
            Ensure64Bit();
            RemovePrinterIfPresent();
            RestartSpooler(delegate
            {
                var path = ProcessorPath();
                if (File.Exists(path))
                    File.Delete(path);
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

        private static string ProcessorPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "spool",
                "prtprocs",
                "x64",
                FactixPaths.ProcessorFileName);
        }

        private static void CopyDll(string source, string destination)
        {
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.Copy(source, destination, true);
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
            uint needed;
            uint returned;
            EnumPrinterDrivers(null, "Windows x64", 1, IntPtr.Zero, 0, out needed, out returned);
            if (needed == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!EnumPrinterDrivers(null, "Windows x64", 1, buffer, needed, out needed, out returned))
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

        private static void EnsureNulPort()
        {
            var defaults = new PrinterDefaults { DesiredAccess = ServerAllAccess };
            if (!OpenPrinterWithDefaults(",XcvMonitor Local Port", out var handle, ref defaults))
                return;

            var bytes = Encoding.Unicode.GetBytes("NUL:\0");
            var input = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, input, bytes.Length);
                uint needed;
                uint status;
                XcvData(handle, "AddPort", input, (uint)bytes.Length, IntPtr.Zero, 0, out needed, out status);
            }
            finally
            {
                Marshal.FreeHGlobal(input);
                ClosePrinter(handle);
            }
        }

        private static void AddFactixPrinter(string driver, string datatype)
        {
            var info = new PrinterInfo2
            {
                pPrinterName = FactixPaths.PrinterName,
                pPortName = "NUL:",
                pDriverName = driver,
                pPrintProcessor = FactixPaths.ProcessorName,
                pDatatype = datatype,
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

        [DllImport("winspool.drv", EntryPoint = "EnumPrinterDriversW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumPrinterDrivers(string? pName, string pEnvironment, uint level, IntPtr pDriverInfo, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

        [DllImport("winspool.drv", EntryPoint = "XcvDataW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool XcvData(IntPtr hXcv, string pszDataName, IntPtr pInputData, uint cbInputData, IntPtr pOutputData, uint cbOutputData, out uint pcbOutputNeeded, out uint pdwStatus);
    }
}
