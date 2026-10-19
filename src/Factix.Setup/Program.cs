using System;

namespace FactixSetup
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var command = args.Length == 0 ? "/install" : args[0];
            try
            {
                if (string.Equals(command, "/uninstall", StringComparison.OrdinalIgnoreCase))
                    PrinterInstaller.Uninstall();
                else if (string.Equals(command, "/install", StringComparison.OrdinalIgnoreCase))
                    PrinterInstaller.Install();
                else
                {
                    Console.Error.WriteLine("Uso: Factix.Setup.exe /install | /uninstall");
                    return 2;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }
}
