using System;
using System.Windows.Forms;

namespace SerialDebugger
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // An unexpected error shows a message instead of closing the app.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => ShowError(e.ExceptionObject as Exception);

            Application.Run(new MainForm());
        }

        private static void ShowError(Exception ex)
        {
            try
            {
                MessageBox.Show("Something went wrong:\n\n" + (ex?.Message ?? "Unknown error"),
                    "Serial Debugger", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception)
            {
                // Nothing more we can do.
            }
        }
    }
}
