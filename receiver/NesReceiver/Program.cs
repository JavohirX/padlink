using System;
using System.Windows.Forms;

namespace NesReceiver;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // Enable high-DPI support
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.Run(new MainForm());
    }
}
