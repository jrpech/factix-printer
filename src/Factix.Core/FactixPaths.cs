using System;
using System.Collections.Generic;
using System.IO;

namespace Factix
{
    public static class FactixPaths
    {
        public const string PrinterName = "Factix";
        public const string ProcessorName = "factixpp";
        public const string ProcessorFileName = "factixpp.dll";

        public static string JobsDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Factix", "jobs");
    }

    public static class PrinterDriverSelection
    {
        public static readonly string[] Preferred =
        {
            "Microsoft PS Class Driver",
            "Microsoft PCL6 Class Driver",
            "Microsoft Enhanced Point and Print Compatibility Driver",
            "Microsoft XPS Document Writer",
            "Generic / Text Only"
        };

        public static string? Choose(IEnumerable<string> installed)
        {
            var available = new HashSet<string>(installed, StringComparer.OrdinalIgnoreCase);
            foreach (var name in Preferred)
            {
                if (available.Contains(name))
                    return name;
            }

            return null;
        }

        public static string DatatypeFor(string? driver)
        {
            if (string.IsNullOrEmpty(driver))
                return "RAW";
            if (driver!.IndexOf("XPS", StringComparison.OrdinalIgnoreCase) >= 0)
                return "RAW";
            if (driver.IndexOf("Text Only", StringComparison.OrdinalIgnoreCase) >= 0)
                return "RAW";
            return "NT EMF 1.008";
        }
    }
}
