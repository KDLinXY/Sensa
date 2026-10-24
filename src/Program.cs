using System;
using System.Reflection;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle("Sensa")]
[assembly: AssemblyProduct("Sensa Mouse Sensitivity")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace Sensa
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        private static void Main()
        {
            bool created;
            _mutex = new Mutex(true, "Local\\Sensa.MouseSensitivity.SingleInstance", out created);
            if (!created)
            {
                MessageBox.Show("Sensa 已在运行。", "Sensa", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                new Application().Run(new MainWindow());
            }
            finally
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        }
    }
}
